COP5615 – Project 4 part 2
Twitter Clone with websockets Submitted by – Yagya Malik, Abhiti Sachdeva
Goal – Implement WebSocket interface to project 4 part 1 Requirements Fulfilled –
• Registration
• Sending tweets
• Mentioning Users and Hashtags in tweets
• Retweets
• Querying tweets by mentions or hashtags
• All of the above happens without querying live.
• Bonus completed, Key agreement happens through ElipticCurve Diffie Hellman Key
exchange protocol, a challenge is formed by the server each time a user attempts to connect to the server. If the challenge is not completed within 1 second the protocol must be tried again.
• HMAC signs every message sent over serialized JSON content Libraries used –
• FSharp.JSON
• WebSocketSharp
• FSharp.Data
• Akka
How to run the code –
• Engine – run dotnet run debug in eng_twitter directory
• User – run dotnet run in sim_twitter directory
Brief description of working of code –
• Web sockets are designed to use restful API, there are different end points for every
request (Each type of request is handled by a different actor on the server side)
• We user websocketsharp to implement web sockets, and each request goes through the appropriate socket like so –
If a user sends a message to register itself, a message would be sent to the server to the appropriate socket number and node id that would serve the request
   
• Once the server has a request, it checks if the user is authenticated with the sever, i.e the server checks if the user is actually registered with our website, and also sends it a random 256 bit challenge and wait for 1 second. The client has to add it’s unix time to the challenge and HMAC (SHA256) it back to the server after encrypting it with it’s private key. If the server is able to verify it with user’s public key then user is allowed to query the server.
• Once the connection has been established, the server will send a JSON reply. Video and Output -
https://youtu.be/m_8U4ERx7z4