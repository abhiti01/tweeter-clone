module engUtil

open System
open System.Collections.Generic
open System.Security.Cryptography
open Akka.Actor
open Akka.FSharp

let mutable authDebugFlag = false

let chalGenerator =
    use rng = RNGCryptoServiceProvider.Create()
    let challenge = Array.zeroCreate<byte> 32
    rng.GetBytes challenge
    challenge |> Convert.ToBase64String

let appendPadding (msg: byte[]) =
    let timeStampPadding = DateTimeOffset.UtcNow.ToUnixTimeSeconds() |> BitConverter.GetBytes
    Array.concat [|msg; timeStampPadding|]

let bytesFromString (str: string) = 
    Text.Encoding.UTF8.GetBytes str



let getHashVal (msg: byte[]) = 
    msg |> SHA256.HashData 

let getPK (pub:byte[]) =
    let objDH = ECDiffieHellman.Create()
    let keySize = objDH.KeySize
    objDH.ImportSubjectPublicKeyInfo((System.ReadOnlySpan pub), (ref keySize))
    objDH.ExportParameters(false)
    
let checkSign (challenge: string) (signature: string) (PK: string) =     
    let ans = challenge |> Convert.FromBase64String |> appendPadding |> getHashVal
    let sign = signature.[0 .. 255] |> Convert.FromBase64String

    let pub = PK |> Convert.FromBase64String
   
    let ecdsaParams = pub |> getPK
    let ecdsa = ECDsa.Create(ecdsaParams)
    ecdsa.VerifyData(ans, sign, HashAlgorithmName.SHA256)


let getSK (serverECDH: ECDiffieHellman) (PK: String) = 
    let pub = PK |> Convert.FromBase64String
    let keySize = serverECDH.KeySize
    let objDH = ECDiffieHellman.Create()
    objDH.ImportSubjectPublicKeyInfo((System.ReadOnlySpan pub), (ref keySize))
    serverECDH.DeriveKeyMaterial(objDH.PublicKey)



let hmacChecker (jsonMessage:string) (signature: string) (sharedSecretKey: byte[]) =
    use hmac = new HMACSHA1(sharedSecretKey)
    if authDebugFlag then
        printfn ""
    let createdSign = jsonMessage |> bytesFromString |> hmac.ComputeHash |> Convert.ToBase64String 
    if authDebugFlag then
        printfn "computed HMAC signature on Server"
        printfn "Json msg HMAC: %A\nComputed HMAC: %A\n" signature createdSign
    signature |> createdSign.Equals

type TweetWithSignature = {
    JsonWithoutSignature : string
    HMACSignature: string
}
type RegisterUser = {
    ReqId : string
    UId : int
    UName : string
    PublicKey : string
}

type Tweet = {
    ReqId : string
    UId : int
    TweetID : string
    Time : DateTime
    Data : string
    HashTag : string
    Mentions : int
    RTs : int
}
type ReplyFromServer = {
    ReqId : string
    Type : string
    Status : string
    MetaData : string option
}


type RegisterReply = {
    ReqId : string
    Type : string
    Status : string
    ServerPK : string
    MetaData: string option
}

type SubscriberInfo = {
    ReqId : string
    UId : int 
    PubId : int
}

type ReplyForTweet = {
    ReqId : string
    Type : string
    Status : int
    Tweet : Tweet
}

type SubReply = {
    ReqId : string
    Type : string
    Target : int
    Sub : int[]
    Pub : int[]
}
type ConnectionResponse = {
    ReqId: string
    Type: string
    Status: string
    Authentication: string
    MetaData: string option
}

type Query = {
    ReqId : string
    UId : int
    HashTag : string
}
type UserConnectionInfo = {
    ReqId : string
    UId : int
    Sign: string
}


type KeyInfo = {
    UserPK: String
    SharedSecretKey: String
    ServerECDH: ECDiffieHellman
}


type RTInfo = {
    ReqId: string
    UId : int
    Target : int
    RTId : string
}



let challengeCache = new Dictionary<int, String>()
let TweetHistoryDb = new Dictionary<int, List<string>>()
let UsersDb = new Dictionary<int, RegisterUser>()
let TagsDb = new Dictionary<string, List<string>>()
let TweetsDb = new Dictionary<string, Tweet>()
let TweetUserDb = new Dictionary<int, List<int>>()
let keysDb = new Dictionary<int, KeyInfo>()
let SubscribersDb = new Dictionary<int, List<int>>()
let MentionsDb = new Dictionary<int, List<string>>()
let helper f = actorOf f
let validUserChecker userID = 
    (UsersDb.ContainsKey(userID)) 

let challengeDB (userID: int) (challenge: string) =
    async{
        challengeCache.Add(userID, challenge)
        do! Async.Sleep 1000
        printfn "Challenge expired"
        challengeCache.Remove(userID) |> ignore
    }
    

let updateUsersDb (updatedData:RegisterUser) =
    let userID = updatedData.UId
    if not (UsersDb.ContainsKey(userID)) then
        UsersDb.Add(userID, updatedData)
        "Success"
    else
        "Fail"

let updateKeysDb (updatedData:RegisterUser) (serverECDH: ECDiffieHellman)=
    keysDb.Add(
        updatedData.UId,
        {
            UserPK = updatedData.PublicKey;
            SharedSecretKey = 
                (getSK serverECDH updatedData.PublicKey) |> Convert.ToBase64String;
            ServerECDH = serverECDH;
        })

let historyUpdater userID tweetId =

    if userID >= 0 && (validUserChecker userID) then        
        if not (TweetHistoryDb.ContainsKey(userID)) then
            let newList = new List<string>()
            newList.Add(tweetId)
            TweetHistoryDb.Add(userID, newList)
        else
            if not (TweetHistoryDb.[userID].Contains(tweetId)) then
                (TweetHistoryDb.[userID]).Add(tweetId)
    
let tagUpdater tag tweetId = 

    if tag <> "" && tag.[0] = '#' then
        if not (TagsDb.ContainsKey(tag)) then
            let newList = new List<string>()
            newList.Add(tweetId)
            TagsDb.Add(tag, newList)
        else
            (TagsDb.[tag]).Add(tweetId)

let updatePubSubDB publisherID subscriberID = 
    let mutable FailureFlag = false

    if publisherID <> subscriberID && (validUserChecker publisherID) && (validUserChecker subscriberID) then

        if not (TweetUserDb.ContainsKey(publisherID)) then
            let newList = new List<int>()
            newList.Add(subscriberID)
            TweetUserDb.Add(publisherID, newList)
        else
            if not ((TweetUserDb.[publisherID]).Contains(subscriberID)) then
                (TweetUserDb.[publisherID]).Add(subscriberID)
            else
                FailureFlag <- true

        if not (SubscribersDb.ContainsKey(subscriberID)) then
            let newList = new List<int>()
            newList.Add(publisherID)
            SubscribersDb.Add(subscriberID, newList)
        else
            if not ((SubscribersDb.[subscriberID]).Contains(publisherID)) then
                (SubscribersDb.[subscriberID]).Add(publisherID)
            else
                FailureFlag <- true
        if FailureFlag then
            "Fail"
        else
            "Success"
    else
        "Fail"

let mentionsUpdater userID tweetId =
    if userID >= 0 && (validUserChecker userID) then
       if not (MentionsDb.ContainsKey(userID)) then
            let newList = new List<string>()
            newList.Add(tweetId)
            MentionsDb.Add(userID, newList)
        else
            (MentionsDb.[userID]).Add(tweetId)

let tweetUpdater (updatedData:Tweet) =
    let tweetId = updatedData.TweetID
    let userID = updatedData.UId
    let tag = updatedData.HashTag
    let mention = updatedData.Mentions
    TweetsDb.Add(tweetId, updatedData)
    historyUpdater userID tweetId
    tagUpdater tag tweetId

    mentionsUpdater mention tweetId
    historyUpdater mention tweetId

    if (TweetUserDb.ContainsKey(userID)) then
        for subscriberID in (TweetUserDb.[userID]) do

            historyUpdater subscriberID tweetId

let updateRT userID (tweetMetaData:Tweet) =
    let newTweetInfo:Tweet = {
        ReqId = tweetMetaData.ReqId ;
        UId  = tweetMetaData.UId ;
        TweetID = tweetMetaData.TweetID ;
        Time = tweetMetaData.Time ;
        Data = tweetMetaData.Data ;
        HashTag = tweetMetaData.HashTag ;
        Mentions = tweetMetaData.Mentions ;
        RTs = (tweetMetaData.RTs+1) ;
    }
    TweetsDb.[tweetMetaData.TweetID] <- newTweetInfo

    historyUpdater userID (tweetMetaData.TweetID)

    if (TweetUserDb.ContainsKey(userID)) then
        for subscriberID in (TweetUserDb.[userID]) do
            historyUpdater subscriberID (tweetMetaData.TweetID)         

let assignTweetID (tweetMetaData:Tweet) =
    let newTweetInfo:Tweet = {
        ReqId = tweetMetaData.ReqId ;
        UId  = tweetMetaData.UId ;
        TweetID = (TweetsDb.Count + 1).ToString() ;
        Time = tweetMetaData.Time ;
        Data = tweetMetaData.Data ;
        HashTag = tweetMetaData.HashTag ;
        Mentions = tweetMetaData.Mentions ;
        RTs = tweetMetaData.RTs ;
    }
    newTweetInfo

let retrieveTopInIDs (subpubMap:Dictionary<int, List<int>>) = 
    let mutable maxCount = 0
    let mutable topID = -1 
    for entry in subpubMap do
        if entry.Value.Count > maxCount then
            maxCount <- (entry.Value.Count)
            topID <- entry.Key
    topID   
let retrieveTopInTags (tagDB:Dictionary<string, List<string>>) =
    let mutable maxCount = 0
    let mutable topTag = ""
    for entry in tagDB do
        if entry.Value.Count > maxCount then
            maxCount <- (entry.Value.Count)
            topTag <- entry.Key
    topTag       
let retrieveTopInMentions (mentionDB:Dictionary<int, List<string>>) =
    let mutable maxCount = 0
    let mutable topMen = -1
    for entry in MentionsDb do
        if entry.Value.Count > maxCount then
            maxCount <- (entry.Value.Count)
            topMen <- entry.Key
    topMen
let retrieveTopInRTs (tweetDB:Dictionary<string, Tweet>) =
    let mutable maxCount = 0
    let mutable topRetweet = ""
    for entry in TweetsDb do
        if entry.Value.RTs > maxCount then
            maxCount <- (entry.Value.RTs)
            topRetweet <- entry.Key
    topRetweet

let viewDb (displayStatus:int) _ =
    if displayStatus = 1 then
        let topPublisher = retrieveTopInIDs TweetUserDb
        let topSubscriber = retrieveTopInIDs SubscribersDb
        let topTag = retrieveTopInTags TagsDb
        let topMention = retrieveTopInMentions MentionsDb
        let topRetweet = retrieveTopInRTs TweetsDb
                
        printfn "\n~~~~~~~~~~~Database Instance~~~~~~~~~~~~"
        printfn "Registered Users Currently: %i" (UsersDb.Keys.Count)
        printfn "Total Tweets on the Platform rn %i" (TweetsDb.Keys.Count)
        if topRetweet <> "" then
            printfn "Top RTed Tweet: %s (%i RTs)" topRetweet (TweetsDb.[topRetweet].RTs)
        if topTag <> "" then
            printfn "Total Tags on the Platform %i" (TagsDb.Keys.Count)
            printfn "Top tagged HashTag: %s (%i times)" topTag (TagsDb.[topTag].Count)
        if topMention >= 0 then
            printfn "Most Mentioned: User%i (%i times)" topMention (MentionsDb.[topMention].Count)
        printfn "~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n"