module api

open System
open System.Security.Cryptography
open simUtil
open UI
open WebSocketSharp
open FSharp.Json
open FSharp.Data
open FSharp.Data.JsonExtensions
open System.Collections.Generic

let mutable authDebugFlag = false


let bytesFromString (str: string) = 
    Text.Encoding.UTF8.GetBytes str

let appendPadding (msg: byte[]) =
    let currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    let timeStampPadding = currentTime |> BitConverter.GetBytes
    if authDebugFlag then
         printfn "Adding timestamp as padding" 
    Array.concat [|msg; timeStampPadding|]

let getHashVal (msg: byte[]) = 
    msg |> SHA256.HashData 

let getPK (pub:byte[]) =
    let objDH = ECDiffieHellman.Create()
    let keySize = objDH.KeySize
    objDH.ImportSubjectPublicKeyInfo((System.ReadOnlySpan pub), (ref keySize))
    objDH.ExportParameters(false)

let retrieveSign (msg: byte[]) (objDH: ECDiffieHellman) =
    let ecdsa = objDH.ExportParameters(true) |> ECDsa.Create
    let hashedMsg = msg |> appendPadding |> getHashVal
    if authDebugFlag then
        printfn "Hashed message: %A" (hashedMsg|>Convert.ToBase64String)
    ecdsa.SignData(hashedMsg, HashAlgorithmName.SHA256) |> Convert.ToBase64String


let getSK (clientECDH: ECDiffieHellman) (PK: String) = 
    let pub = PK |> Convert.FromBase64String
    let keySize = clientECDH.KeySize
    let temporary = ECDiffieHellman.Create()
    temporary.ImportSubjectPublicKeyInfo((System.ReadOnlySpan pub), (ref keySize))
    clientECDH.DeriveKeyMaterial(temporary.PublicKey)

let retrieveHMACSign (jsonMessage: string) (sharedSecretKey: byte[]) =    
    use hmac = new HMACSHA1(sharedSecretKey)
    jsonMessage |> bytesFromString |> hmac.ComputeHash |> Convert.ToBase64String


let createWebsocketDB serverWebsocket =
    let wssActorDB = new Dictionary<string, WebSocket>()
    (wssActorDB.Add("RegisterUser", new WebSocket(serverWebsocket      +   "/registerthisuser")))
    (wssActorDB.Add("postTweet", new WebSocket(serverWebsocket     +   "/tweetthis/sendthis")))
    (wssActorDB.Add("RTthis", new WebSocket(serverWebsocket       +   "/tweetthis/retweetthis")))
    (wssActorDB.Add("SubscribeTo", new WebSocket(serverWebsocket     +   "/subscribeto")))
    (wssActorDB.Add("ConnectTo", new WebSocket(serverWebsocket       +   "/connectto")))
    (wssActorDB.Add("DisconnectTo", new WebSocket(serverWebsocket    +   "/disconn")))
    (wssActorDB.Add("PrevQueries", new WebSocket(serverWebsocket  +   "/tweetthis/querythis")))
    (wssActorDB.Add("QueryMention", new WebSocket(serverWebsocket  +   "/mentionthis/querythis")))
    (wssActorDB.Add("TagMention", new WebSocket(serverWebsocket      +   "/tagthis/querythis")))
    (wssActorDB.Add("QuerySubscribe", new WebSocket(serverWebsocket +  "/subscribeto/querythis")))
    wssActorDB
    
let enableWss (webServerSocketDB:Dictionary<string, WebSocket>) =
    if (not webServerSocketDB.["postTweet"].IsAlive) then (webServerSocketDB.["postTweet"].Connect())
    if (not webServerSocketDB.["RTthis"].IsAlive) then (webServerSocketDB.["RTthis"].Connect())
    if (not webServerSocketDB.["SubscribeTo"].IsAlive) then (webServerSocketDB.["SubscribeTo"].Connect())
    if (not webServerSocketDB.["DisconnectTo"].IsAlive) then (webServerSocketDB.["DisconnectTo"].Connect())
    if (not webServerSocketDB.["PrevQueries"].IsAlive) then (webServerSocketDB.["PrevQueries"].Connect())
    if (not webServerSocketDB.["QueryMention"].IsAlive) then (webServerSocketDB.["QueryMention"].Connect())
    if (not webServerSocketDB.["TagMention"].IsAlive) then (webServerSocketDB.["TagMention"].Connect())
    if (not webServerSocketDB.["QuerySubscribe"].IsAlive) then (webServerSocketDB.["QuerySubscribe"].Connect())

let disableWss (webServerSocketDB:Dictionary<string, WebSocket>) =
    if (webServerSocketDB.["postTweet"].IsAlive) then (webServerSocketDB.["postTweet"].Close())
    if (webServerSocketDB.["RTthis"].IsAlive) then (webServerSocketDB.["RTthis"].Close())
    if (webServerSocketDB.["SubscribeTo"].IsAlive) then (webServerSocketDB.["SubscribeTo"].Close())
    if (webServerSocketDB.["DisconnectTo"].IsAlive) then (webServerSocketDB.["DisconnectTo"].Close())
    if (webServerSocketDB.["PrevQueries"].IsAlive) then (webServerSocketDB.["PrevQueries"].Close())
    if (webServerSocketDB.["QueryMention"].IsAlive) then (webServerSocketDB.["QueryMention"].Close())
    if (webServerSocketDB.["TagMention"].IsAlive) then (webServerSocketDB.["TagMention"].Close())
    if (webServerSocketDB.["QuerySubscribe"].IsAlive) then (webServerSocketDB.["QuerySubscribe"].Close())

let regCallback (nameOfNode, webServerSocketDB:Dictionary<string,WebSocket>, SimulationModeFlag:bool) = fun (msg:MessageEventArgs) ->
    let replyInfo = (Json.deserialize<RegisterReply> msg.Data)
    let SuccessFlag = if (replyInfo.Status = "Success") then (true) else (false)

    if SuccessFlag then
        enableWss (webServerSocketDB)
        if SimulationModeFlag then 
            printfn "[%s] User \"%s\" has been registered and logged in" nameOfNode (replyInfo.MetaData.Value)
        else 
        serverPublicKey <- replyInfo.ServerPK
        if authDebugFlag then
            printfn "\n\nRecieved PK: %A" serverPublicKey
        UserLoginSuccessfulFlag <- Success
    else
        if SimulationModeFlag then printfn "[%s] Unable to register User!\n" nameOfNode
        else UserLoginSuccessfulFlag <- Fail
    // Close the session for /registerthisuser
    webServerSocketDB.["RegisterUser"].Close()

let connectCallback (NodeId, webServerSocketDB:Dictionary<string,WebSocket>, objDH: ECDiffieHellman) = fun (msg:MessageEventArgs) ->
    let replyInfo = (Json.deserialize<ConnectionResponse> msg.Data)
    match replyInfo.Status with
    | "Success" -> 
        enableWss (webServerSocketDB)
        
        webServerSocketDB.["ConnectTo"].Close()
        if authDebugFlag then
            printfn "[User%i] Login Successful!" NodeId

        let (queryMsg:Query) = {
            ReqId = "PrevQueries" ;
            UId = (replyInfo.MetaData.Value|> int) ;
            HashTag = "" ;
        }
        webServerSocketDB.["PrevQueries"].Send(Json.serialize queryMsg)
        UserLoginSuccessfulFlag <- Success
    | "Auth" -> 
        let challenge = replyInfo.Authentication |> Convert.FromBase64String
        if authDebugFlag then
            printfn "Challenge Received: %A" replyInfo.Authentication
            printfn "adding padding and signature....\n"
        let signature = retrieveSign challenge objDH
        if authDebugFlag then 
            printfn "The signature : %A" signature
        let (authMsg:UserConnectionInfo) = {
            UId = replyInfo.MetaData.Value|> int;
            ReqId = "Auth";
            Sign = signature;
        }
        webServerSocketDB.["ConnectTo"].Send(Json.serialize authMsg)
    | _ ->
        UserLoginSuccessfulFlag <- Fail
        printBanner (sprintf "Faild to connect UId: %i\nError msg: %A" NodeId (replyInfo.MetaData.Value))
        webServerSocketDB.["ConnectTo"].Close()

let disconnectCallback (nameOfNode, webServerSocketDB:Dictionary<string,WebSocket>) = fun (msg:MessageEventArgs) ->
    disableWss (webServerSocketDB)
    UserLoginSuccessfulFlag <- Success


let replyCallback (nameOfNode) = fun (msg:MessageEventArgs) ->
    let replyInfo = (Json.deserialize<ReplyFromServer> msg.Data)
    let SuccessFlag = if (replyInfo.Status = "Success") then (true) else (false)
    if SuccessFlag then
        UserLoginSuccessfulFlag <- Success
        printBanner (sprintf "[%s] %s" nameOfNode (replyInfo.MetaData.Value))
    else 
        UserLoginSuccessfulFlag <- Fail
        printBanner (sprintf "[%s] [Error]\n%s" nameOfNode (replyInfo.MetaData.Value))

let printTweet msg = 
    let tweetReplyInfo = (Json.deserialize<ReplyForTweet> msg)
    let tweetInfo = tweetReplyInfo.Tweet
    printfn "\n~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~"
    printfn "Index: %i      Time: %s" (tweetReplyInfo.Status) (tweetInfo.Time.ToString())
    printfn "Posted by: %i" (tweetInfo.UId)
    let mentionStr = if (tweetInfo.Mentions < 0) then "@N/A" else ("@User"+tweetInfo.Mentions.ToString())
    let tagStr = if (tweetInfo.HashTag = "") then "#N/A" else (tweetInfo.HashTag)
    printfn "Data: {%s}\n%s  %s  RTs: %i" (tweetInfo.Data) (tagStr) (mentionStr) (tweetInfo.RTs)
    printfn "TweetID: %s" (tweetInfo.TweetID)

let printSubscribe msg nameOfNode =
    let subReplyInfo = (Json.deserialize<SubReply> msg)
    printfn "\n~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~"
    printfn "Name: %s" ("User" + (subReplyInfo.Target.ToString()))
    printf "Subscribe To: "
    for id in subReplyInfo.Sub do
        printf "User%i " id
    printf "\nPublish To: "
    for id in subReplyInfo.Pub do
        printf "User%i " id
    printfn "\n"

let queryCallback (nameOfNode) = fun (msg:MessageEventArgs) ->
    let  jsonMsg = JsonValue.Parse(msg.Data)
    let  qT = jsonMsg?Type.AsString()
    if qT = "ShowTweet" then
        printTweet (msg.Data)
    else if qT = "ShowSub" then 
        printSubscribe (msg.Data) (nameOfNode)
    else
        let SuccessFlag = if (jsonMsg?Status.AsString() = "Success") then (true) else (false)
        if SuccessFlag then 
            UserLoginSuccessfulFlag <- Success
            printBanner (sprintf "[%s]\n%s" nameOfNode (jsonMsg?MetaData.AsString()))
        else 
            UserLoginSuccessfulFlag <- Fail
            printBanner (sprintf "[%s]\n%s" nameOfNode (jsonMsg?MetaData.AsString()))




let sendRegMsgToServer (msg:string, SimulationModeFlag, wssReg:WebSocket, NodeId, PK) =
    wssReg.Connect()
    if SimulationModeFlag then
        let registerMessage:RegJson = { 
            ReqId = "RegisterUser" ; 
            UId = NodeId ; 
            UName = "User"+ (NodeId.ToString()) ; 
            PublicKey = PK ;
        }
        let data = (Json.serialize registerMessage)
        wssReg.Send(data)
    else
        let msg = Json.deserialize<RegJson> msg
        let registerMessage:RegJson = { 
            ReqId = msg.ReqId; 
            UId = msg.UId; 
            UName = msg.UName ; 
            PublicKey = PK ;
        }
        let data = (Json.serialize registerMessage)
        wssReg.Send(data)
   

let sendRequestToServer (msg:string, qT, webServerSocketDB:Dictionary<string,WebSocket>, nameOfNode) =
    if not (webServerSocketDB.[qT].IsAlive) then
        if qT = "DisconnectTo" then
            webServerSocketDB.[qT].Connect()
            webServerSocketDB.[qT].Send(msg)
            printBanner (sprintf "[%s]\nDisconnect from the server..." nameOfNode)
            UserLoginSuccessfulFlag <- SessionTimeout
        else
            UserLoginSuccessfulFlag <- SessionTimeout
    else 
        webServerSocketDB.[qT].Send(msg)

let postTweetToServer (msg:string, ws:WebSocket, nameOfNode, objDH: ECDiffieHellman) =
    if not (ws.IsAlive) then
        UserLoginSuccessfulFlag <- SessionTimeout        
    else 
        let key = getSK objDH serverPublicKey

        let signature = retrieveHMACSign msg key
        if authDebugFlag then
            printfn "Generating secret key"
            printfn "Secret key:\n %A" (key|>Convert.ToBase64String)
            printfn "Original message: %A" msg
            printfn "HMAC sign the message first"
            printfn "HMAC signature: %A" signature
        let (signedMsg:TweetWithSignature) = {
            JsonWithoutSignature = msg
            HMACSignature = signature
        }
        let data = (Json.serialize signedMsg)
        ws.Send(data)