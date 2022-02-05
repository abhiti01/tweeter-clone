module sim

open simUtil
open System
open Akka.FSharp
open System.Diagnostics
open Akka.Actor

open FSharp.Json
let register (client: IActorRef) = 
    client <! """{"ReqId":"RegisterUser"}"""
let postTweet (client: IActorRef) (hashtag: string) (mention: int)= 
    let (request: Tweet) = {
        ReqId = "postTweet";
        UId = (int) client.Path.Name;
        TweetID = "";
        Time = DateTime.Now;
        Data = "Tweeeeeet";
        HashTag = hashtag;
        Mentions = mention;
        RTs = 0 ;
    }
    client <! (Json.serialize request)
let subscribeTo (client: IActorRef) (publisher: IActorRef) = 
    let (request: SubscriberInfo) = {
        ReqId = "SubscribeTo";
        UId = (int) client.Path.Name;
        PubId = (int) publisher.Path.Name;
    }
    client <! (Json.serialize request)
let retweet (client: IActorRef) (targetUserID: int)=
    let (request: RTInfo) = {
        ReqId = "RTthis";
        RTId = "";
        Target = targetUserID;
        UId = (int) client.Path.Name;
    }
    client <! (Json.serialize request)
let connect (client: IActorRef) = 
    let (request: UserConnectionInfo) = {
        ReqId = "ConnectTo";
        UId = client.Path.Name |> int;
        Sign = "";
    }
    client <! (Json.serialize request)
let disconnect (client: IActorRef) = 
    let (request: UserConnectionInfo) = {
        ReqId = "DisconnectTo";
        UId = client.Path.Name |> int;
        Sign = ""
    }
    client <! (Json.serialize request)

let prevQueries (client: IActorRef) = 
    let (request: Query) = {
        ReqId = "PrevQueries";
        UId = client.Path.Name |> int;
        HashTag = "";
    }
    client <! (Json.serialize request)
let mentionQueries (client: IActorRef) (mentionedUserID: int) = 
    let (request: Query) = {
        ReqId = "PrevQueries";
        HashTag = "";
        UId = mentionedUserID;
    }
    client <! (Json.serialize request)
let tagQueries (client: IActorRef) (tag: string)= 
    let (request: Query) = {
        ReqId = "TagMention";
        HashTag = tag;
        UId = 0;
    }
    client <! (Json.serialize request)
let queryBySubscribtion (client: IActorRef) (id: int) = 
    let (request: Query) = {
            ReqId = "QuerySubscribe";
            HashTag = "";
            UId = id;
        }
    client <! (Json.serialize request)

let spawnClients (system) (noOfClients: int) nodeOfClient = 
    [1 .. noOfClients]
    |> List.map (fun id -> spawn system ((string) id) nodeOfClient)
    |> List.toArray

let arraySampler (arr: 'T []) (num: int) = 
    if arr.Length = 0 then 
        List.empty
    else
        let randomnumgenerator = System.Random()    
        Seq.initInfinite (fun _ -> randomnumgenerator.Next (arr.Length)) 
        |> Seq.distinct
        |> Seq.take(num)
        |> Seq.map (fun i -> arr.[i]) 
        |> Seq.toList

let shuffle (rand: Random) (l) = 
    l |> Array.sortBy (fun _ -> rand.Next()) 

let fetchNumberSubscribers (numClients: int)= 
    let constant = List.fold (fun acc i -> acc + (1.0/i)) 0.0 [1.0 .. (float) numClients]
    let res =
        [1.0 .. (float) numClients] 
        |> List.map (fun x -> (float) numClients/(x*constant) |> Math.Round |> int)
        |> List.toArray
    //Environment.Exit 1
    shuffle (Random()) res             

let sampleTags (hashtags: string []) = 
    let random = Random()
    let rand () = random.Next(hashtags.Length-1)
    hashtags.[rand()]

let fetchCurrConnected (connections: bool []) =
    [1 .. connections.Length-1]
    |> List.filter (fun i -> connections.[i])
    |> List.toArray

let fetchCurrDisconnected (connections: bool []) =
    [1 .. connections.Length-1]
    |> List.filter (fun i -> not connections.[i])
    |> List.toArray
(* Simulator parameter variables *)
let mutable numClients = 1000
let mutable maxCycle = 1000
let mutable totalRequest = 2147483647
let hashtags = [|"#COP";"#5615"; "#DOSP"; "#Twitter"; "#abhiti"; "#yagya"|]

let mutable percentActive = 60
let mutable percentpostTweet = 50
let mutable percentRetweet = 20
let mutable percentQueryHistory = 20
let mutable percentQueryByMention = 10
let mutable percentQueryByTag = 10

let mutable numActive = numClients * percentActive / 100
let mutable numpostTweet = numActive * percentpostTweet / 100
let mutable numRetweet = numActive * percentRetweet / 100
let mutable numQueryHistory = numActive * percentQueryHistory / 100
let mutable numQueryByMention = numActive * percentQueryByMention / 100
let mutable numQueryByTag = numActive * percentQueryByTag / 100

let getSimualtionParamFromUser () =
    printfn "\n\nSIMULATION MODE ACTIVATED\n"
    printfn "enter parameters:"

    printf "How many Users?\n"
    numClients <- userInputScan "int" |> int

    printf "Percentof Users active?\n "
    percentActive <- userInputScan "int" |> int
    numActive <- numClients * percentActive / 100

    printf "Percent of active Users that can post tweets?\n " 
    percentpostTweet <- userInputScan "int" |> int
    numpostTweet <- numActive * percentpostTweet / 100

    printf "Percent of active Users that retweet?\n " 
    percentRetweet <- userInputScan "int" |> int
    numRetweet <- numActive * percentRetweet / 100

    let mutable remaining = 100    
    printf "Percentof active Users that query hist? (max: %d)\n " remaining
    percentQueryHistory <- userInputScan "int" |> int
    numQueryHistory <- numActive * percentQueryHistory / 100
    remaining <- remaining - percentQueryHistory

    printf "Percent of active Users qthat query by mention? (max: %d)\n " remaining
    percentQueryByMention<- userInputScan "int" |> int
    numQueryByMention <- numActive * percentQueryByMention / 100
    remaining <- remaining - percentQueryByMention

    printf "Percent of active Users that query by tag? (max: %d)\n " remaining
    percentQueryByTag<- userInputScan "int" |> int
    numQueryByTag <- numActive * percentQueryByTag / 100
    remaining <- remaining - percentQueryByTag

    printf "Total API requests to stop the simulation?\n "
    totalRequest <- userInputScan "int" |> int

    printf "How many cycles of simulations do you want?\n "
    maxCycle <- userInputScan "int" |> int



let startSimulation (system) (timer:Stopwatch) (nodeOfClient) = 
    printfn "\n\n\n\n\n
    -----------------------------Simulation MODE settings given--------------------------------\n
    This is you simulation settings...\n
    Num of Users: %d\n
    Num of total requests: %d\n
    Num of repeated cycles: %d\n
    Num of active users: %d (%d%%)\n
    Num of active users that post a tweet: %d (%d%%)\n
    Num of active users that retweet: %d (%d%%)\n
    Num of active users that can query hist: %d (%d%%)\n
    Num of active users that can query by mention: %d (%d%%)\n
    Num of active users that can query by Tag: %d (%d%%)\n
    -----------------------------------------------------------------------------"
        numClients totalRequest maxCycle numActive percentActive numpostTweet 
        percentpostTweet numRetweet percentRetweet numQueryHistory percentQueryHistory
        numQueryByMention percentQueryByMention numQueryByTag percentQueryByTag

    printfn "\n\nPress enter to start simulation\n"
    Console.ReadLine() |> ignore

    (* 1. spawn clients *)
    let myClients = spawnClients system numClients nodeOfClient

    (* 2. register all clients *)
    let numOfSub = fetchNumberSubscribers numClients
    
    myClients 
    |> Array.map(fun client -> 
        async{
            register client
        })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore
    
    System.Threading.Thread.Sleep (max numClients 3000)

    myClients
    |> Array.mapi(fun i client ->
        async {
            let sub = numOfSub.[i]
            let mutable s = Set.empty
            let rand = Random()
            while s.Count < sub do
                let subscriber = rand.Next(numClients-1)
                if (myClients.[subscriber].Path.Name) <> (client.Path.Name) && not (s.Contains(subscriber)) then
                    s <- s.Add(subscriber)
                    subscribeTo (myClients.[subscriber]) client
        })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore
    
    myClients
    |> Array.mapi (fun i client ->
        async{
            let rand1 = Random()
            let rand2 = Random(rand1.Next())
            let numTweets = max (rand1.Next(1,5) * numOfSub.[i]) 1
            
            for i in 1 .. numTweets do
                postTweet client (sampleTags hashtags) (rand2.Next(numClients))
        })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore

    myClients
    |> Array.map (fun client -> 
        async{
            disconnect client
        })
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore

    System.Threading.Thread.Sleep (max numClients 2000)
    printfn "\n\nSetup completed!"
    printfn "\nPress Enter to begin simulation cycles\n\n"
    Console.ReadLine() |> ignore

    printfn "----------------------------- Simulation ----------------------------"
    printfn " maxCycle: %d" maxCycle
    printfn "------------------------------------------------------------------------------"
    
    timer.Start()
    let timer = new Timers.Timer(1000.)
    let event = Async.AwaitEvent (timer.Elapsed) |> Async.Ignore
    let connections = Array.create (numClients+1) false
    let mutable cycle = 0
    timer.Start()
    while cycle < maxCycle do
        printfn "----------------------------- CYCLE NO %d ------------------------------------" cycle
        cycle <- cycle + 1
        Async.RunSynchronously event

        let connecting = (fetchCurrConnected connections)
        let numToDisonnect = ((float) connecting.Length) * 0.3 |> int

        arraySampler connecting numToDisonnect        
        |> List.map (fun clientID -> 
            async{
                disconnect myClients.[clientID-1]
                connections.[clientID] <- false
            })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore
                 
        
        let disconnecting = (fetchCurrDisconnected connections)
        let numToConnect = ((float) disconnecting.Length) * 0.31 |> int
        
        arraySampler disconnecting numToConnect
        |> List.map (fun clientID -> 
            async{
                connect myClients.[clientID-1]
                connections.[clientID] <- true
            })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore

        printfn "length:%d Connecting: %A" (fetchCurrConnected connections).Length (fetchCurrConnected connections)

        let rand = Random()
        arraySampler (fetchCurrConnected connections) numpostTweet
        |> List.map (fun clientID -> 
                async{
                    postTweet myClients.[clientID-1] (sampleTags hashtags) (rand.Next(1, numClients))
            })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore 
         
        let rand = Random()
        arraySampler (fetchCurrConnected connections) numRetweet
        |> List.map (fun clientID -> 
                async{
                    retweet myClients.[clientID-1] (rand.Next(1,numClients))
            })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore 

        let rand = Random()
        let shuffledConnects = fetchCurrConnected connections |> shuffle rand
        shuffledConnects.[0 .. numQueryHistory-1]
        |> Array.map (fun clientID -> 
                async{
                    prevQueries myClients.[clientID-1]
            })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore 
        let rand = Random()
        shuffledConnects.[numQueryHistory .. (numQueryHistory + numQueryByMention - 1)]
        |> Array.map (fun clientID -> 
                async{
                    mentionQueries myClients.[clientID-1] (rand.Next(1,numClients))
            })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore 
        shuffledConnects.[(numQueryHistory + numQueryByMention) .. (numQueryHistory + numQueryByMention + numQueryByTag - 1)]
        |> Array.map (fun clientID -> 
                async{
                    tagQueries myClients.[clientID-1] (sampleTags hashtags)
            })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> ignore 
      


    timer.Stop()
    System.Threading.Thread.Sleep (max numClients 2000)
    printfn "\n\n %i cycles of simulation completed!]" maxCycle
    printfn "Total time %A" timer.Elapsed
    Console.ReadLine() |> ignore
    printfn "Total time %A" timer.Elapsed
    Environment.Exit 1
        
