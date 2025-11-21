# Quickstart Guide

Get SignalR Chat running locally in 5 minutes without any Azure dependencies!

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Git
- Web browser

## 5-Minute Setup

### Step 1: Clone and Build

```bash
# Clone the repository
git clone https://github.com/smereczynski/SignalR-Chat.git
cd SignalR-Chat

# Build the solution
dotnet build ./src/Chat.sln
```

### Step 2: Run with In-Memory Mode

```bash
# Run the application (in-memory storage, no Azure required)
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

You should see output like:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5099
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

### Step 3: Open in Browser

1. Open http://localhost:5099 in your browser
2. You'll see the login page

### Step 4: Login with OTP

**Available users**: alice, bob, charlie, dave, eve

1. Select a user (e.g., **alice**)
2. Click "Send Code"
3. Check the **terminal output** for the OTP code:

```
info: Chat.Web.Services.RedisOtpStore[0]
      OTP code for alice: 123456
```

4. Enter the 6-digit code
5. Click "Verify & Login"
6. You're in! 🎉

### Step 5: Join a Room and Chat

1. Select a room: **General**, **Tech**, **Random**, or **Sports**
2. Type a message and press Enter
3. Open another browser window (or incognito tab)
4. Login as a different user (e.g., **bob**)
5. Join the same room
6. See messages in real-time!

## What's Running?

In-memory mode uses:
- ✅ **In-memory OTP storage** (no Redis needed)
- ✅ **In-memory database** (no Cosmos DB needed)
- ✅ **Direct SignalR connections** (no Azure SignalR Service)
- ✅ **All features work** except persistence across restarts

## Features to Try

### Real-time Messaging
- Send messages and see them appear instantly
- Messages are delivered to all users in the room

### Read Receipts
- Send a message as one user
- Login as another user in the same room
- See "Delivered" appear below your message
- Scroll to the message as the second user
- See "Read by [username]" appear

### Typing Indicators
- Start typing as one user
- Watch the "X is typing..." indicator appear for other users
- Stops after 3 seconds of inactivity

### Presence Tracking
- See online users in the sidebar
- Open/close browser tabs to see users go online/offline

### Reconnection
- Send a message
- Turn off your Wi-Fi
- See the room title change to "(RECONNECTING…)"
- Turn Wi-Fi back on
- See automatic reconnection (exponential backoff)
- Note: If connection cannot be restored within 60 seconds, the UI shows "(DISCONNECTED)" but reconnection attempts continue in the background

### Language Switching
- Click the flag icon in the profile section
- Choose from 9 languages:
  - 🇺🇸 English
  - 🇵🇱 Polish
  - 🇪🇸 Spanish
  - 🇫🇷 French
  - 🇩🇪 German
  - 🇮🇹 Italian
  - 🇵🇹 Portuguese
  - 🇯🇵 Japanese
  - 🇨🇳 Chinese

## Limitations of In-Memory Mode

| Feature | In-Memory | With Azure |
|---------|-----------|------------|
| Real-time messaging | ✅ | ✅ |
| Read receipts | ✅ | ✅ |
| Presence tracking | ✅ | ✅ |
| OTP authentication | ✅ | ✅ |
| **Persistence** | ❌ Lost on restart | ✅ Persisted |
| **Multi-instance** | ❌ Single server | ✅ Load balanced |
| **Notifications** | ❌ No SMS/email | ✅ Via ACS |
| **Scalability** | ❌ Limited | ✅ Thousands of users |

## Stopping the Application

Press `Ctrl+C` in the terminal to stop the server.

## Next Steps

### Want Persistence?

Set up Azure resources for full functionality:

➡️ **[Full Installation Guide](installation.md)** - Set up Cosmos DB, Redis, and Azure

### Want to Develop?

Set up your development environment:

➡️ **[Local Development Setup](../development/local-setup.md)** - VS Code, debugging, hot reload

### Want to Deploy?

Deploy to Azure for production:

➡️ **[Azure Deployment Guide](../deployment/azure/)** - Deploy with Bicep IaC

### Want to Contribute?

Help improve the project:

➡️ **[Contributing Guide](../../CONTRIBUTING.md)** - How to contribute

## Troubleshooting

### Port 5099 Already in Use

Change the port:

```bash
dotnet run --project ./src/Chat.Web --urls=http://localhost:5100
```

### OTP Code Not Showing in Terminal

Check that you're looking at the correct terminal window where you ran `dotnet run`.

### Can't Login

Make sure you:
1. Selected a valid user (alice, bob, charlie, dave, eve)
2. Copied the exact 6-digit code from the terminal
3. Entered the code within 5 minutes

### Build Errors

```bash
# Clean and rebuild
dotnet clean ./src/Chat.sln
dotnet build ./src/Chat.sln
```

## FAQ

**Q: Can I use a custom username?**  
A: In in-memory mode, only the 5 fixed users work. With Cosmos DB, you can add users to the database.

**Q: How long do OTP codes last?**  
A: 5 minutes (configurable via `Otp__OtpTtlSeconds` in appsettings.json)

**Q: Can I use this in production?**  
A: In-memory mode is for development only. Use Azure resources for production.

**Q: Where are messages stored?**  
A: In in-memory mode, messages are stored in application memory (lost on restart). With Cosmos DB, messages are persisted.

---

**🎉 Congratulations!** You now have SignalR Chat running locally.

**Next**: [Full Installation Guide](installation.md) | [Back to docs](../README.md)
