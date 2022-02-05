open System
open System.Security.Cryptography
open System.Globalization
open System.Text
open System.Collections.Generic
open Akka.Actor
open Akka.FSharp
open System.Diagnostics
open api

open FSharp.Data
open FSharp.Data.JsonExtensions
open FSharp.Json

open sim
open simUtil
open UI

open WebSocketSharp

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



let system = ActorSystem.Create("UserInterface")
let serverWebsocket = "ws://localhost:8003"
let timer = Stopwatch()

let nodeOfClient (SimulationModeFlag) (clientMailbox:Actor<string>) =
    let mutable nameOfNode = "User" + clientMailbox.Self.Path.Name
    let mutable NodeId = 
        match (Int32.TryParse(clientMailbox.Self.Path.Name)) with
        | (true, value) -> value
        | (false, _) -> 0
    let nodeCrypto = ECDiffieHellman.Create()
    let nodePublicKey = nodeCrypto.ExportSubjectPublicKeyInfo() |> Convert.ToBase64String

    let webServerSocketDB = createWebsocketDB (serverWebsocket)

    (webServerSocketDB.["RegisterUser"]).OnMessage.Add(regCallback (nameOfNode, webServerSocketDB, SimulationModeFlag))

    (webServerSocketDB.["postTweet"]).OnMessage.Add(replyCallback (nameOfNode))
    (webServerSocketDB.["RTthis"]).OnMessage.Add(replyCallback (nameOfNode))
    (webServerSocketDB.["SubscribeTo"]).OnMessage.Add(replyCallback (nameOfNode))
    (webServerSocketDB.["PrevQueries"]).OnMessage.Add(queryCallback (nameOfNode))
    (webServerSocketDB.["QueryMention"]).OnMessage.Add(queryCallback (nameOfNode))
    (webServerSocketDB.["TagMention"]).OnMessage.Add(queryCallback (nameOfNode))
    (webServerSocketDB.["QuerySubscribe"]).OnMessage.Add(queryCallback (nameOfNode))
    (webServerSocketDB.["DisconnectTo"]).OnMessage.Add(disconnectCallback (nameOfNode, webServerSocketDB))
    (webServerSocketDB.["ConnectTo"]).OnMessage.Add(connectCallback (NodeId, webServerSocketDB, nodeCrypto))

    let rec loop() = actor {
        let! (msg: string) = clientMailbox.Receive()
        let  jsonMessageRec = JsonValue.Parse(msg)
        let  qT = jsonMessageRec?ReqId.AsString()
        match qT with
            | "RegisterUser" ->                
                sendRegMsgToServer (msg,SimulationModeFlag, webServerSocketDB.[qT], NodeId, nodePublicKey)
            | "postTweet" ->
                postTweetToServer (msg, webServerSocketDB.["postTweet"], nameOfNode, nodeCrypto)
            | "RTthis" | "SubscribeTo"
            | "PrevQueries" | "QueryMention" | "TagMention" | "QuerySubscribe" 
            | "DisconnectTo" ->
                sendRequestToServer (msg, qT, webServerSocketDB, nameOfNode)        
            | "ConnectTo" ->
                let impCon = webServerSocketDB.["ConnectTo"]
                impCon.Connect()
                impCon.Send(msg)

            | "UserModeOnFlag" ->
                let currentUId = jsonMessageRec?CurUserID.AsInteger()
                NodeId <- currentUId
                nameOfNode <- "User" + currentUId.ToString()

            | _ ->
                printfn "node \"%s\" received unknown\"%s\"" nameOfNode qT
                Environment.Exit 1
         
        return! loop()
    }
    loop()




[<EntryPoint>]
let main argv =
    try
        timer.Start()
        let modeSelected = argv.[0]

        if modeSelected = "user" then
            
            let terminalRef = spawn system "-Terminal" (nodeOfClient false)
            startUserInterface terminalRef

        else if modeSelected = "debug" then
            printfn "\n\nShowing auth msgs:\n"
            authDebugFlag <- true
            let terminalRef = spawn system "-Terminal" (nodeOfClient false)
            startUserInterface terminalRef

        else
            printfn "\n\nIncorrect arguments provided!!\n Available args: \n\t1. dotnet run simulate\n\t2. dotnet run user\n\t3. dotnet run debug\n"
            Environment.Exit 1

          
    with | :? IndexOutOfRangeException ->
            printfn "\n\nIncorrect arguments provided!!\n Available args: \n1. dotnet run user\n2. dotnet run debug\n\n"

         | :? FormatException ->
            printfn "\nIncorrect Format\n"


    0 