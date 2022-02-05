module updateUtil

open System
open System.Security.Cryptography
open Akka.Actor
open Akka.FSharp
open WebSocketSharp.Server
open engUtil
open FSharp.Data
open FSharp.Data.JsonExtensions
open FSharp.Json

let mutable authDebugFlag = false

let bytesFromString (str: string) = 
    Text.Encoding.UTF8.GetBytes str


let chalGenerator =
    use rng = RNGCryptoServiceProvider.Create()
    let challenge = Array.zeroCreate<byte> 32
    rng.GetBytes challenge
    challenge |> Convert.ToBase64String

let appendPadding (msg: byte[]) =
    let timeStampPadding = DateTimeOffset.UtcNow.ToUnixTimeSeconds() |> BitConverter.GetBytes
    Array.concat [|msg; timeStampPadding|]

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

type ActorMsg = 
| WsockToActor of string * WebSocketSessionManager * string

type ConActorMsg =
| SocketConnectorActor of string * WebSocketSessionManager * string
| AutoConnect of int

type RegActorMsg =
| WsockToRegActor of string * IActorRef * WebSocketSessionManager * string

type LoginActorMsg =
| WsockToLoginActor of string * IActorRef * WebSocketSessionManager * string
let registerActor (serverMailbox:Actor<RegActorMsg>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: RegActorMsg) = serverMailbox.Receive()

        match msg with
        | WsockToRegActor (msg, connectionActorRef, sessionActor, sid) ->
            let registerMessage = (Json.deserialize<RegisterUser> msg)
            let status = updateUsersDb registerMessage
            let serverECDH = ECDiffieHellman.Create()
            let serverPublicKey = 
                serverECDH.ExportSubjectPublicKeyInfo() |> Convert.ToBase64String            
                
            let reply:RegisterReply = { 
                ReqId = "Reply" ;
                Type = "RegisterUser" ;
                Status =  status ;
                ServerPK = serverPublicKey;
                MetaData = Some (registerMessage.UId.ToString());
            }
            let data = (Json.serialize reply)
            sessionActor.SendTo(data,sid)

            if status = "Success" then
                if authDebugFlag then
                    printfn "Generating server public key for User%i: %A....Please Wait" registerMessage.UId serverPublicKey
                    printfn "\n[%s] DB updated with Keys" nameOfNode
                updateKeysDb registerMessage serverECDH 
                connectionActorRef <! AutoConnect (registerMessage.UId)

        return! loop()
    }
    loop()     

let subscribeActor (serverMailbox:Actor<ActorMsg>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: ActorMsg) = serverMailbox.Receive()

        match msg with
        | WsockToActor (msg, sessionActor, sid) ->
            let subInfo = (Json.deserialize<SubscriberInfo> msg)
            let status = updatePubSubDB (subInfo.PubId) (subInfo.UId)
            let mutable descStr = ""
            if status = "Success" then
                descStr <- "Successfully subscribe to User " + (subInfo.PubId.ToString())
            else
                descStr <- "Failed to subscribe to User " + (subInfo.PubId.ToString())


            let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "SubscribeTo" ;
                    Status =  status ;
                    MetaData =  Some descStr ;
            }
            let data = (Json.serialize reply)
            sessionActor.SendTo(data,sid)

        return! loop()
    }
    loop() 
let tweetActor (serverMailbox:Actor<ActorMsg>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: ActorMsg) = serverMailbox.Receive()

        match msg with
        | WsockToActor (msg, sessionActor, sid) ->
            let m = (Json.deserialize<TweetWithSignature> msg)
            let unsignedJson = m.JsonWithoutSignature
            let tInfo = Json.deserialize<Tweet> unsignedJson
            let sharedSecretKey = 
                keysDb.[tInfo.UId].SharedSecretKey |> Convert.FromBase64String
            let tweetInfo = tInfo |> assignTweetID
            if not (validUserChecker tweetInfo.UId) then
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "postTweet" ;
                    Status =  "Failed" ;
                    MetaData =  Some "The user should be registered before sending a Tweet" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
            elif not (hmacChecker unsignedJson m.HMACSignature sharedSecretKey) then
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "postTweet" ;
                    Status =  "Failed" ;
                    MetaData =  Some "Cannot pass HMAC authentication" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
            else                
                tweetUpdater tweetInfo
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "postTweet" ;
                    Status =  "Success" ;
                    MetaData =  Some "Successfully send a Tweet to Server" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)            
                

        return! loop()
    }
    loop()
let retweetActor (serverMailbox:Actor<ActorMsg>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: ActorMsg) = serverMailbox.Receive()

        match msg with
        | WsockToActor (msg, sessionActor, sid) ->
            let retweetInfo = (Json.deserialize<RTInfo> msg)
            let rtID = retweetInfo.RTId
            let userID = retweetInfo.UId
            let tUserID = retweetInfo.Target
            let mutable FailureFlag = false
            if rtID = "" then
                if (validUserChecker tUserID) && TweetHistoryDb.ContainsKey(tUserID) && TweetHistoryDb.[tUserID].Count > 0 then
                    let randomnumgenerator = Random()
                    let numTweet = TweetHistoryDb.[tUserID].Count
                    let rndIdx = randomnumgenerator.Next(numTweet)
                    let targetReTweetID = TweetHistoryDb.[tUserID].[rndIdx]
                    let retweetInfo = TweetsDb.[targetReTweetID]
                    if (retweetInfo.UId <> userID) then
                        updateRT userID retweetInfo
                    else
                        FailureFlag <- true
                else
                    FailureFlag <- true
            else
                if TweetsDb.ContainsKey(rtID) then
                    if (TweetsDb.[rtID].UId) <> userID then
                        updateRT userID (TweetsDb.[rtID])
                    else
                        FailureFlag <- true
                else
                    FailureFlag <- true
            if FailureFlag then
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "postTweet" ;
                    Status =  "Failed" ;
                    MetaData =  Some "RTthis failed, can't find the specified Tweet ID or due to author rule" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
            else
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "postTweet" ;
                    Status =  "Success" ;
                    MetaData =  Some "Successfully retweet the Tweet!" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)

        return! loop()
    }
    loop()