open System
open System.Diagnostics
open System.Security.Cryptography
open System.Globalization
open System.Collections.Generic
open System.Text
open Akka.Actor
open Akka.FSharp

open Dashboard
open engUtil
open WebSocketSharp.Server
open updateUtil
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

let getHashVal (msg: byte[]) = 
    msg |> SHA256.HashData 

let appendPadding (msg: byte[]) =
    let timeStampPadding = DateTimeOffset.UtcNow.ToUnixTimeSeconds() |> BitConverter.GetBytes
    Array.concat [|msg; timeStampPadding|]

let getPK (pub:byte[]) =
    let objDH = ECDiffieHellman.Create()
    let keySize = objDH.KeySize
    objDH.ImportSubjectPublicKeyInfo((System.ReadOnlySpan pub), (ref keySize))
    objDH.ExportParameters(false)


let getSK (serverECDH: ECDiffieHellman) (PK: String) = 
    let pub = PK |> Convert.FromBase64String
    let keySize = serverECDH.KeySize
    let objDH = ECDiffieHellman.Create()
    objDH.ImportSubjectPublicKeyInfo((System.ReadOnlySpan pub), (ref keySize))
    serverECDH.DeriveKeyMaterial(objDH.PublicKey)

let checkSign (challenge: string) (signature: string) (PK: string) =     
    let ans = challenge |> Convert.FromBase64String |> appendPadding |> getHashVal
    let sign = signature.[0 .. 255] |> Convert.FromBase64String

    let pub = PK |> Convert.FromBase64String
   
    let ecdsaParams = pub |> getPK
    let ecdsa = ECDsa.Create(ecdsaParams)
    ecdsa.VerifyData(ans, sign, HashAlgorithmName.SHA256)


let hmacChecker (jsonMessage:string) (signature: string) (sharedSecretKey: byte[]) =
    use hmac = new HMACSHA1(sharedSecretKey)
    if authDebugFlag then
        printfn ""
    let createdSign = jsonMessage |> bytesFromString |> hmac.ComputeHash |> Convert.ToBase64String 
    if authDebugFlag then
        printfn "computed HMAC signature on Server"
        printfn "Json msg HMAC: %A\nComputed HMAC: %A\n" signature createdSign
    signature |> createdSign.Equals
let system = ActorSystem.Create("TwitterEngine")

let serverWorkers = 1000
let spawnServerWorker (noOfClients: int) = 
    [1 .. noOfClients]
    |> List.map (fun id -> (spawn system ("Q"+id.ToString()) queryActorNode))
    |> List.toArray
let serverProcessWorker = spawnServerWorker serverWorkers
let getRandomWorker () =
    let randomnumgenerator = Random()
    serverProcessWorker.[randomnumgenerator.Next(serverWorkers)]


let mutable onlineUserSet = Set.empty
let onlineUserUpdater userID option = 
    let isConnected = onlineUserSet.Contains(userID)
    if option = "connect" && not isConnected then
        if validUserChecker userID then
            onlineUserSet <- onlineUserSet.Add(userID)
            0
        else
            -1
    else if option = "disconnect" && isConnected then
        onlineUserSet <- onlineUserSet.Remove(userID)
        0
    else
        0

let connectionActor (serverMailbox:Actor<ConActorMsg>) =
    let nameOfNode = serverMailbox.Self.Path.Name
    let rec loop() = actor {

        let! (msg: ConActorMsg) = serverMailbox.Receive()
        match msg with
        | SocketConnectorActor (msg, sessionActor, sid) ->
            let connectionInfo = (Json.deserialize<UserConnectionInfo> msg)
            let userID = connectionInfo.UId
            let qT = connectionInfo.ReqId           
            
            match qT with
            | "ConnectTo" ->
                
                if not (onlineUserSet.Contains(userID)) && validUserChecker userID then
                    let challange = chalGenerator
                    challengeDB userID challange |> Async.Start
                    if authDebugFlag then
                        printfn ""
                        printfn "timestamp %A\n" (DateTime.Now)
                    let (reply:ConnectionResponse) = { 
                        ReqId = "Reply" ;
                        Type = qT ;
                        Status =  "Auth" ;
                        Authentication = challange;
                        MetaData =  Some (userID.ToString());
                    }
                    let data = (Json.serialize reply)
                    sessionActor.SendTo(data,sid)  
                else 
                    let (reply:ConnectionResponse) = { 
                        ReqId = "Reply" ;
                        Type = qT ;
                        Status =  "Fail" ;
                        Authentication = "";
                        MetaData =  Some ("Register First!!!!") ;
                    }
                    let data = (Json.serialize reply)
                    sessionActor.SendTo(data,sid)                         
            | "Auth" ->
                let keyInfo = keysDb.[userID]
                if (challengeCache.ContainsKey userID) then
                    let answer = challengeCache.[userID]
                    let signature = connectionInfo.Sign
                    if authDebugFlag then
                        printfn ""
                        printfn ""
                        printfn ""
                    if (checkSign answer signature keyInfo.UserPK) then
                        let (reply:ConnectionResponse) = { 
                            ReqId = "Reply" ;
                            Type = qT ;
                            Status =  "Success" ;
                            Authentication = "";
                            MetaData =  Some (userID.ToString());
                        }
                        let data = (Json.serialize reply)
                        sessionActor.SendTo(data,sid)  
                    else
                        printfn "\n[Authentication failed!!!! for User%i\n" userID
                        let (reply:ConnectionResponse) = { 
                            ReqId = "Reply" ;
                            Type = qT ;
                            Status =  "Fail" ;
                            MetaData =  Some "Authentication failed!" ;
                            Authentication = "";
                        }
                        let data = (Json.serialize reply)
                        sessionActor.SendTo(data,sid)
                else
                    let (reply:ConnectionResponse) = { 
                        ReqId = "Reply" ;
                        Type = qT ;
                        Status =  "Fail" ;
                        MetaData =  Some "Authentication failed!" ;
                        Authentication = "";
                    }
                    let data = (Json.serialize reply)
                    sessionActor.SendTo(data,sid)
            | _ ->
                (onlineUserUpdater userID "disconnect") |> ignore
                let (reply:ReplyFromServer) = { 
                    ReqId = "Reply" ;
                    Type = qT ;
                    Status =  "Success" ;
                    MetaData =   Some (userID.ToString()) ;
                }
                let data = (Json.serialize reply)
                sessionActor.SendTo(data,sid)
        | AutoConnect userID ->
            let ret = onlineUserUpdater userID "connect"
            if ret< 0 then printfn "[userID:%i] Auto connectfailed " userID
            else printfn "[UId:%i] Auto connect success!" userID

        return! loop()
    }
    loop() 


let wss = WebSocketServer("ws://localhost:8003")

let regActorRef = spawn system "RegisterUser-DB-Worker" registerActor
let tweetActorRef = spawn system "AddTweet-DB-Worker" tweetActor
let retweetActorRef = spawn system "ReTweet-DB-Worker" retweetActor
let subscriveActorRef = spawn system "SubscribeTo-DB-Worker" subscribeActor
let connectionActorRef = spawn system "Connection-DB-Worker" connectionActor
let queryHisActorRef = spawn system "QHistory-DB-Worker" historyActor
let queryMenActorRef = spawn system "QMention-DB-Worker" queryMentionActor
let queryTagActorRef = spawn system "QTag-DB-Worker" queryTagActor
let querySubActorRef = spawn system "QSub-DB-Worker" querySubActor


type RegisterUser () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/registerthisuser]\nData:%s\n" msg.Data 
        regActorRef <! WsockToRegActor (msg.Data, connectionActorRef, wssm.Sessions, wssm.ID)


type Tweet () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/tweetthis/sendthis]\nData:%s\n" msg.Data 
        tweetActorRef <! WsockToActor (msg.Data, wssm.Sessions, wssm.ID)

type RTthis () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/tweetthis/retweetthis]\nData:%s\n"  msg.Data 
        retweetActorRef <! WsockToActor (msg.Data, wssm.Sessions, wssm.ID)

type SubscribeTo () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/subscribeto]\nData:%s\n" msg.Data 
        subscriveActorRef <! WsockToActor (msg.Data,wssm.Sessions,wssm.ID)

type Connection () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/connection]\nData:%s\n" (msg.Data)
        connectionActorRef <! SocketConnectorActor (msg.Data,wssm.Sessions,wssm.ID)

type QueryHis () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/tweetthis/querythis]\nData:%s\n" msg.Data
        queryHisActorRef <! ImpActor (msg.Data, getRandomWorker(), wssm.Sessions, wssm.ID)

type QueryMen () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/mentionthis/querythis]\nData:%s\n" msg.Data
        queryMenActorRef <! ImpActor (msg.Data, getRandomWorker(), wssm.Sessions, wssm.ID)

type TagMention () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/tagthis/querythis]\nData:%s\n" msg.Data
        queryTagActorRef <! ImpActor (msg.Data, getRandomWorker(), wssm.Sessions, wssm.ID)
type QuerySub () =
    inherit WebSocketBehavior()
    override wssm.OnMessage msg = 
        printfn "\n[/subscribeto/querythis]\nData:%s\n" msg.Data
        querySubActorRef <! ImpActor (msg.Data, getRandomWorker() , wssm.Sessions, wssm.ID)




[<EntryPoint>]
let main argv =
    try
        if argv.Length <> 0 then
            authDebugFlag <- 
                match (argv.[0]) with
                | "debug" -> true
                | _ -> false

        wss.AddWebSocketService<RegisterUser> ("/registerthisuser")
        wss.AddWebSocketService<Tweet> ("/tweetthis/sendthis")
        wss.AddWebSocketService<RTthis> ("/tweetthis/retweetthis")
        wss.AddWebSocketService<SubscribeTo> ("/subscribeto")
        wss.AddWebSocketService<Connection> ("/connectto")
        wss.AddWebSocketService<Connection> ("/disconn")
        wss.AddWebSocketService<QueryHis> ("/tweetthis/querythis")
        wss.AddWebSocketService<QueryMen> ("/mentionthis/querythis")
        wss.AddWebSocketService<TagMention> ("/tagthis/querythis")
        wss.AddWebSocketService<QuerySub> ("/subscribeto/querythis")
        wss.Start ()
        printfn "\n~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~"
        if authDebugFlag then
            printfn "Twitter Engine has been turned ON in debug Mode\n "
        else
            printfn "Twitter Engine has been turned ON"
        printfn "~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n"
        Console.ReadLine() |> ignore
        wss.Stop()
 

    with | :? IndexOutOfRangeException ->
            printfn "\nIncorrect Input\n"

         | :?  FormatException ->
            printfn ""


    0 // return an integer exit code
