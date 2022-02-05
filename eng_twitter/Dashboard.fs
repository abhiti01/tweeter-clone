module Dashboard

open FSharp.Data
open FSharp.Data.JsonExtensions
open FSharp.Json
open System
open System.Text
open System.Collections.Generic
open Akka.Actor
open Akka.FSharp
open System.Diagnostics
open engUtil
open WebSocketSharp.Server


type QueryWorkerMsg =
| PrevQueries of string * WebSocketSessionManager * string * string[]
| TagMention of string * WebSocketSessionManager * string * string[]
| QueryMention of string * WebSocketSessionManager * string * string[]

type ServerQuery = 
| ImpActor of string * IActorRef * WebSocketSessionManager * string


let historyActor (serverMailbox:Actor<ServerQuery>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: ServerQuery) = serverMailbox.Receive()
        match msg with
        | ImpActor (msg, workerRef ,sessionActor, sid) ->
            let queryInfo = (Json.deserialize<Query> msg)
            let userID = queryInfo.UId
            if not (TweetHistoryDb.ContainsKey(userID)) then
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = queryInfo.ReqId ;
                    Status =  "NoTweet" ;
                    MetaData =  Some "Nothing to display" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
            else
                workerRef <! PrevQueries (msg, sessionActor, sid, TweetHistoryDb.[userID].ToArray())
        return! loop()
    }
    loop() 

let queryMentionActor (serverMailbox:Actor<ServerQuery>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: ServerQuery) = serverMailbox.Receive()
        match msg with
        | ImpActor (msg, workerRef ,sessionActor, sid) ->
            let queryInfo = (Json.deserialize<Query> msg)
            let userID = queryInfo.UId
            if not (MentionsDb.ContainsKey(userID)) then
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = queryInfo.ReqId ;
                    Status =  "NoTweet" ;
                    MetaData =  Some "Nothing to display" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
            else
                workerRef <! PrevQueries (msg, sessionActor, sid, MentionsDb.[userID].ToArray())
        return! loop()
    }
    loop() 

let queryTagActor (serverMailbox:Actor<ServerQuery>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: ServerQuery) = serverMailbox.Receive()
        match msg with
        | ImpActor (msg, workerRef ,sessionActor, sid) ->
            let queryInfo = (Json.deserialize<Query> msg)
            let tag = queryInfo.HashTag
            if not (TagsDb.ContainsKey(tag)) then
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = queryInfo.ReqId ;
                    Status =  "NoTweet" ;
                    MetaData =  Some "Nothing to display" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
            else
                workerRef <! PrevQueries (msg, sessionActor, sid, TagsDb.[tag].ToArray())
        return! loop()
    }
    loop() 

let querySubActor (serverMailbox:Actor<ServerQuery>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: ServerQuery) = serverMailbox.Receive()
        match msg with
        | ImpActor (msg, _ ,sessionActor, sid) ->
            let queryInfo = (Json.deserialize<Query> msg)
            let userID = queryInfo.UId
            if not (SubscribersDb.ContainsKey(userID)) && not (TweetUserDb.ContainsKey(userID))then
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "PrevQueries" ;
                    Status =  "NoTweet" ;
                    MetaData =  Some ("Nothing to display")  ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
            else if (SubscribersDb.ContainsKey(userID)) && not (TweetUserDb.ContainsKey(userID))then
                let subReply:SubReply = {
                    ReqId = "Reply" ;
                    Type = "ShowSub" ;
                    Target = userID ;
                    Sub = SubscribersDb.[userID].ToArray() ;
                    Pub = [||] ;
                }
                let data = (Json.serialize subReply)
                sessionActor.SendTo(data,sid)
            else if not (SubscribersDb.ContainsKey(userID)) && (TweetUserDb.ContainsKey(userID))then
                let subReply:SubReply = {
                    ReqId = "Reply" ;
                    Type = "ShowSub" ;
                    Target = userID ;
                    Sub = [||] ;
                    Pub = TweetUserDb.[userID].ToArray() ;
                }
                let data = (Json.serialize subReply)
                sessionActor.SendTo(data,sid)
            else 
                let subReply:SubReply = {
                    ReqId = "Reply" ;
                    Type = "ShowSub" ;
                    Target = userID ;
                    Sub = SubscribersDb.[userID].ToArray() ;
                    Pub = TweetUserDb.[userID].ToArray() ;
                }
                let data = (Json.serialize subReply)
                sessionActor.SendTo(data,sid)     
        return! loop()
    }
    loop() 


let queryActorNode (mailbox:Actor<QueryWorkerMsg>) =
    let nameOfNode = "QueryActor " + mailbox.Self.Path.Name
    let rec loop() = actor {
        let! (msg: QueryWorkerMsg) = mailbox.Receive()
       
        match msg with
            | PrevQueries (json, sessionActor, sid, tweetIDarray) ->
                let  jsonMsg = JsonValue.Parse(json)
                let  userID = jsonMsg?UId.AsInteger()
                let mutable tweetCount = 0
                for tweetId in (tweetIDarray) do
                    if TweetsDb.ContainsKey(tweetId) then
                        tweetCount <- tweetCount + 1
                        let tweetReply:ReplyForTweet = {
                            ReqId = "Reply" ;
                            Type = "ShowTweet" ;
                            Status = tweetCount ;
                            Tweet = TweetsDb.[tweetId] ;
                        }
                        let data = (Json.serialize tweetReply)
                        sessionActor.SendTo(data,sid)

                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "PrevQueries" ;
                    Status =  "Success" ;
                    MetaData =  Some "Query history Tweets done" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)

            | TagMention (json, sessionActor, sid, tweetIDarray) ->
                let jsonMsg = JsonValue.Parse(json)
                let tag = jsonMsg?HashTag.AsString()

                let mutable tweetCount = 0
                for tweetId in tweetIDarray do
                    if TweetsDb.ContainsKey(tweetId) then
                        tweetCount <- tweetCount + 1
                        
                        let tweetReply:ReplyForTweet = {
                            ReqId = "Reply" ;
                            Type = "ShowTweet" ;
                            Status = tweetCount ;
                            Tweet = TweetsDb.[tweetId] ;
                        }
                        let data = (Json.serialize tweetReply)
                        sessionActor.SendTo(data,sid)
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "PrevQueries" ;
                    Status =  "Success" ;
                    MetaData =  Some ("Query Tweets with "+tag+ " done") ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)

            | QueryMention (json, sessionActor, sid,tweetIDarray) ->
                let  jsonMsg = JsonValue.Parse(json)
                let  userID = jsonMsg?UId.AsInteger()
                let  qT = jsonMsg?ReqId.AsString()
                let mutable tweetCount = 0
                for tweetId in (tweetIDarray) do
                    if TweetsDb.ContainsKey(tweetId) then
                        tweetCount <- tweetCount + 1
                        let tweetReply:ReplyForTweet = {
                            ReqId = "Reply" ;
                            Type = "ShowTweet" ;
                            Status = tweetCount ;
                            Tweet = TweetsDb.[tweetId] ;
                        }
                        let data = (Json.serialize tweetReply)
                        sessionActor.SendTo(data,sid)

                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = "PrevQueries" ;
                    Status =  "Success" ;
                    MetaData =  Some "Query mentioned Tweets done" ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
        return! loop()
    }
    loop()