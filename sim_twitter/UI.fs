module UI
open simUtil
open System
open Akka.FSharp
type CheckUserStatus =
| Success
| Fail
| Waiting
| Timeout
| SessionTimeout

let mutable (UserLoginSuccessfulFlag:CheckUserStatus) = Waiting
let mutable (serverPublicKey:string) = ""
let printBanner (printStr:string) =
    printfn "\n~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~"
    printfn "%s" printStr
    printfn "~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n"

let showPrompt option currentUId= 
    match option with
    | "loginFirst" ->
        printfn "USER MODE ACTIVATED, you can register as a new user or login as existing user\n"
        printfn "Please choose from the following(num/text):"
        printfn "1. register\t (register a new Twitter account)"
        printfn "2. connect\t (login as a User)"
        printfn "3. exit\t\t (end this program)"
        printf ">"
    | "afterLogin" ->
        printfn "\n\"User%i\" logged in\n" currentUId
        printfn "Please choose from the following(num/text):"
        printfn "1. postTweet\t (Post a Tweet)"
        printfn "2. retweet\t (RT a Tweet)"
        printfn "3. subscribe\t (Subscribe To) to a User"
        printfn "4. disconnect\t (log out)"
        printfn "5. history\t (View a particular users tweets)"
        printfn "6. tag\t\t (View tweets with a particular hashtag)"
        printfn "7. Querybymention\t (View tweets with a mentioned user)"
        printfn "8. Querybysubscribe\t (Check if subscribed to a particularuser)"
        printfn "9. exit\t\t (end this program)"
        printf ">"
    | _ ->
        ()


let setTimeout _ =
    UserLoginSuccessfulFlag <- Timeout


let waitForServerResponse (timeout:float) =
    let timer = new Timers.Timer(timeout*1000.0)
    UserLoginSuccessfulFlag <- Waiting
    timer.Elapsed.Add(setTimeout)
    timer.Start()
    printBanner "Waiting for a response from the server...."
    while UserLoginSuccessfulFlag = Waiting do ()
    timer.Close()

let waitServerResPlusAutoLogoutCheck (timeout:float, command:string) =
    waitForServerResponse timeout
    if UserLoginSuccessfulFlag = SessionTimeout then
        printBanner (sprintf "session timeout!\n reconnect to the server!!")



let startUserInterface terminalRef =    

    let mutable currentUId = -1
    let mutable curState= 0
    while true do
        while curState = 0 do
            (showPrompt "loginFirst" currentUId)
            let inputStr = Console.ReadLine()
            match inputStr with
                | "1" | "register" ->
                    let requestJSON = regJSONGenerator "key"
                    let tmpuserID = getUId requestJSON
                    terminalRef <! requestJSON
  
                    waitForServerResponse (5.0)
                    if UserLoginSuccessfulFlag = Success then
                        printBanner ("Successfully registered and logged in User"+ tmpuserID.ToString())
                        terminalRef <! """{"ReqId":"UserModeOnFlag", "CurUserID":"""+"\""+ tmpuserID.ToString() + "\"}"
                        currentUId <- tmpuserID
                        curState <- 1
                        (showPrompt "afterLogin" currentUId)
                    else if UserLoginSuccessfulFlag = Fail then
                        printBanner (sprintf "Faild to register User: %i\nUser already exists!!" tmpuserID)

                    else
                        printBanner ("Faild to registerUser: " + tmpuserID.ToString() + "\n(timeout)")


                | "2" | "connect" ->
                    let requestJSON = connDisonnJSONGenerator ("ConnectTo", -1)
                    let tmpuserID = getUId requestJSON
                    terminalRef <! requestJSON

                    waitForServerResponse (5.0)
                    if UserLoginSuccessfulFlag = Success then
                        printBanner ("Successfully connected and logged in User"+ tmpuserID.ToString())
                        terminalRef <! """{"ReqId":"UserModeOnFlag", "CurUserID":"""+"\""+ tmpuserID.ToString() + "\"}"
                        currentUId <- tmpuserID
                        curState <- 1
                        (showPrompt "afterLogin" currentUId)
                    else if UserLoginSuccessfulFlag = Fail then
                        ()

                    else
                        printBanner ("Faild to connect and login User: " + tmpuserID.ToString() + "\n(Server no response, timeout occurs)")


                | "3" | "exit" | "ex" ->
                    printBanner "Sad to see you go ... BYE-BYE"
                    Environment.Exit 1
                | _ ->
                    ()


        while curState = 1 do
            let inputStr = Console.ReadLine()
            match inputStr with
                | "1"| "postTweet" ->
                    terminalRef <! tweetJSONGenerator currentUId
                    waitServerResPlusAutoLogoutCheck (5.0, "postTweet")

                | "2"| "retweet" -> 
                    terminalRef <! rtJSONGenerator currentUId
                    waitServerResPlusAutoLogoutCheck (5.0, "retweet")

                | "3"| "subscribe" | "sub" -> 
                    terminalRef <! subJSONGenerator currentUId
                    waitServerResPlusAutoLogoutCheck (5.0, "subscribe")

                | "4" | "disconnect" ->
                    terminalRef <! connDisonnJSONGenerator ("DisconnectTo", currentUId)
                    waitForServerResponse (5.0)
                    if UserLoginSuccessfulFlag = Success then
                        printBanner ("Successfully diconnected and logged out User"+ currentUId.ToString())
                        currentUId <- -1
                        curState <- 0
                    else if UserLoginSuccessfulFlag = SessionTimeout then
                        currentUId <- -1
                        curState <- 0
                        // (showPrompt "loginFirst" currentUId)

                | "5"| "history" -> 
                    terminalRef <! queryJSONGenerator "PrevQueries"
                    waitServerResPlusAutoLogoutCheck (10.0, "PrevQueries")

                | "6"| "tag" -> 
                    terminalRef <! queryJSONGenerator "TagMention"
                    waitServerResPlusAutoLogoutCheck (5.0, "TagMention")

                | "7"| "mention" | "men" -> 
                    terminalRef <! queryJSONGenerator "QueryMention"
                    waitServerResPlusAutoLogoutCheck (5.0, "QueryMention")

                | "8"| "Qsubscribe" | "Qsub" -> 
                    terminalRef <! queryJSONGenerator "QuerySubscribe"
                    waitServerResPlusAutoLogoutCheck (5.0, "QuerySubscribe")

                | "9" | "exit" | "ex" ->
                    terminalRef <! connDisonnJSONGenerator ("DisconnectTo", currentUId)
                    waitForServerResponse (5.0)
                    printBanner "BYE BYE"
                    Environment.Exit 1
                | _ ->
                    (showPrompt "afterLogin" currentUId)
                    ()
