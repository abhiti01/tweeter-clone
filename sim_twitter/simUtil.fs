module simUtil

open System
open System.Security.Cryptography
(* For Json Libraries *)
open FSharp.Data
open FSharp.Data.JsonExtensions
open FSharp.Json

type RegisterReply = {
    ReqId : string
    Type : string
    Status : string
    ServerPK : string
    MetaData : string option
}

type ReplyFromServer = {
    ReqId : string
    Type : string
    Status : string
    MetaData : string option
}

type TweetWithSignature = {
    JsonWithoutSignature: string
    HMACSignature: string
}

type RegJson = {
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

type ReplyForTweet = {
    ReqId : string
    Type : string
    Status : int
    Tweet : Tweet
}

type SubscriberInfo = {
    ReqId : string
    UId : int 
    PubId : int
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

type UserConnectionInfo = {
    ReqId : string
    UId : int
    Sign: string
}

type RTInfo = {
    ReqId: string
    UId : int
    Target : int
    RTId : string
}

type Query = {
    ReqId : string
    UId : int
    HashTag : string
}

let userInputScan (option:string) = 
    let mutable PromptFlag = true
    let mutable userInput = ""
    match option with
    | "int" ->
        while PromptFlag do
            printf "Enter a number: "
            userInput <- Console.ReadLine()
            match (Int32.TryParse(userInput)) with
            | (true, _) -> (PromptFlag <- false)
            | (false, _) ->  printfn "Invalid number"
        userInput
    | "string" ->
        while PromptFlag do
            printf "Enter a string: "
            userInput <- Console.ReadLine()
            match userInput with
            | "" | "\n" | "\r" | "\r\n" | "\0" -> printfn "Invalid string"
            | _ -> (PromptFlag <- false)
        userInput
    | "YesNo" ->
        while PromptFlag do
            printf "Enter yes/no: "
            userInput <- Console.ReadLine()
            match userInput.ToLower() with
            | "yes" | "y" -> 
                (PromptFlag <- false) 
                userInput<-"yes"
            | "no" | "n" ->
                (PromptFlag <- false) 
                userInput<-"no"
            | _ -> printfn "[Error] Invalid input"
        userInput
    | _ ->
        userInput                                
    


let regJSONGenerator (PK:string) =
    
    printfn "Pleae enter a unique num for UserID: "
    let uid = (int) (userInputScan "int")
    printfn "Pleae enter your Name : "
    let username = (userInputScan "string")
    let regJSON:RegJson = { 
        ReqId = "RegisterUser" ; 
        UId =  uid ;
        UName = username ; 
        PublicKey = PK;
    }
    Json.serialize regJSON

let connDisonnJSONGenerator (option:string, currentUId:int) = 
    if option = "ConnectTo" then
        printfn "Pleae enter a unique num for UserID: "
        let uid = (int) (userInputScan "int")
        let connectJSON:UserConnectionInfo = {
            ReqId = "ConnectTo" ;
            UId = uid ;
            Sign = "";
        }
        Json.serialize connectJSON
    else
        let connectJSON:UserConnectionInfo = {
            ReqId = "DisconnectTo" ;
            UId = currentUId ;
            Sign = "";
        }
        Json.serialize connectJSON

let tweetJSONGenerator currentUId = 
    let mutable tag = ""
    let mutable mention = -1
    printfn "type your tweet:"
    let content = (userInputScan "string")
    printfn "add a HashTag?"
    if (userInputScan "YesNo") = "yes" then
        printfn "enter HashTag (with #): "
        tag <- (userInputScan "string")
    printfn "add a Mention?"
    if (userInputScan "YesNo") = "yes" then
        printfn "enter UserID for mention (w/o @): "
        mention <- (int) (userInputScan "int")

    let (tweetJSON:Tweet) = {
        ReqId = "postTweet" ;
        UId  = currentUId ;
        TweetID = "" ;
        Time = (DateTime.Now) ;
        Data = content ;
        HashTag = tag ;
        Mentions = mention ;
        RTs = 0 ;
    }
    Json.serialize tweetJSON

let subJSONGenerator currentUId = 
    printfn "enter the UserID for subscribe: "
    let subToUserID = (int) (userInputScan "int")
    let (subJSON:SubscriberInfo) = {
        ReqId = "SubscribeTo" ;
        UId = currentUId ;
        PubId = subToUserID;
    }
    Json.serialize subJSON
let rtJSONGenerator currentUId = 
    printfn "enter TweetID for RT: "
    let rtID = (userInputScan "string")
    let (retweetJSON:RTInfo) = {
        ReqId = "RTthis" ;
        UId  = currentUId ;
        Target =  -1 ;
        RTId = rtID ;
    }
    Json.serialize retweetJSON

let queryJSONGenerator (option:string) =
    match option with
    | "TagMention" ->
        printfn "enter the HashTag (with #): "
        let tag = userInputScan "string"
        let (queryTagJSON:Query) = {
            ReqId = "TagMention" ;
            UId = -1 ;
            HashTag = tag ;
        }
        Json.serialize queryTagJSON
    | "PrevQueries" | "QueryMention" | "QuerySubscribe" ->
        printfn "Enter UserID:"
        let uid = (int) (userInputScan "int")
        let (queryJSON:Query) = {
            ReqId = option ;
            UId = uid ;
            HashTag = "" ;
        }
        Json.serialize queryJSON
    | _ -> 
        printfn "[Error] queryJSONGenerator function wrong input"
        Environment.Exit 1
        ""

let getUId (jsonStr:string) = 
    let jsonMsg = JsonValue.Parse(jsonStr)
    (jsonMsg?UId.AsInteger())