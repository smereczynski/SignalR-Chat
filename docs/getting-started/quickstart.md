# Quickstart Guide

Bring SignalR Chat up locally and validate the current dispatch-center workflow.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- Web browser

## Quick Bring-Up

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
Testing__InMemory=true dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

**⚠️ Important**: The `Testing__InMemory=true` environment variable is **required** to run without Azure dependencies. Without it, the application will attempt to connect to Azure services if connection strings are configured.

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

### Step 4: Bootstrap the Current Model

This branch does not seed users, rooms, or dispatch-center topology.

Before chat can work, create:

1. one user with `userName`, `upn`, `dispatchCenterId`, and `enabled = true`
2. at least two dispatch centers linked through `correspondingDispatchCenterIds`
3. at least one officer on both dispatch centers via `officerUserNames`

Use the canonical bootstrap guidance here:

- [Local Setup](../development/local-setup.md)
- [Bootstrap](../deployment/bootstrap.md)

### Step 5: Login with OTP

1. Enter the `userName` of an existing provisioned user
2. Click "Send Code"
3. Check the **terminal output** for the OTP code:

```
[OTP] User=your-user Dest=... Code=123456
```

4. Enter the 6-digit code
5. Click "Verify & Login"
6. Open the derived pair room shown for that user's dispatch center

### Step 6: Verify Pair Chat

1. Open the pair room derived from your dispatch-center topology
2. Type a message and press Enter
3. Open another browser window or incognito tab
4. Login as a different user from the counterpart dispatch center
5. Open the same derived pair room
6. Confirm real-time message delivery and read state updates

## What's Running?

With `Testing__InMemory=true`, the application uses:
- ✅ **In-memory OTP storage** (no Redis needed)
- ✅ **In-memory database** (no Cosmos DB needed)
- ✅ **Direct SignalR connections** (no Azure SignalR Service)
- ✅ **Current chat and escalation flows work** after manual bootstrap
- ✅ **No network dependencies** - Works completely offline

Important: `Testing__InMemory=true` removes Azure dependencies, but it does not recreate the old fixed-user or fixed-room demo model.

**Without `Testing__InMemory=true`**:
- ❌ Attempts to connect to Azure Cosmos DB (if connection string exists)
- ❌ Attempts to connect to Azure SignalR Service (if connection string exists)
- ❌ Attempts to connect to Redis (if connection string exists)
- ❌ Will fail to start if Azure resources are not available

## Features to Try

### Real-time Messaging
- Send messages and see them appear instantly
- Messages are delivered to users in the same derived pair room

### Read Receipts
- Send a message as one dispatch-center user
- Login as a user from the counterpart dispatch center in the same pair room
- See "Delivered" appear below your message
- Scroll to the message as the second user
- See "Read by [username]" appear

### Escalations
- Send a message and leave it unread from the counterpart dispatch center
- Wait for the automatic escalation window to expire
- Confirm escalation state changes in the room
- Try manual escalation for your own unacknowledged messages

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
- Choose from 8 languages:
      - 🇺🇸 English
      - 🇵🇱 Polish
      - 🇩🇪 German
      - 🇨🇿 Czech
      - 🇸🇰 Slovak
      - 🇺🇦 Ukrainian
      - 🇱🇹 Lithuanian
      - 🇷🇺 Russian

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

## Current Branch Constraints

- There are no seeded users.
- There are no seeded standard rooms.
- Rooms are derived from dispatch-center topology.
- A user may authenticate successfully and still see no rooms if bootstrap is incomplete.

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
1. provisioned the user record before trying OTP
2. used the exact `userName` stored in the application user record
3. copied the exact 6-digit code from the terminal
4. entered the code within 5 minutes

### Login Works But Chat Is Empty

This means the authentication path works, but dispatch-center bootstrap is incomplete.

Check:

1. the user has `dispatchCenterId`
2. the assigned dispatch center exists
3. the dispatch center has a corresponding partner
4. both sides have at least one officer in `officerUserNames`

### Build Errors

```bash
# Clean and rebuild
dotnet clean ./src/Chat.sln
dotnet build ./src/Chat.sln
```

## FAQ

**Q: Can I use a custom username?**  
A: Yes. The current branch expects manually provisioned users. There is no fixed demo-user list anymore.

**Q: How long do OTP codes last?**  
A: 5 minutes (configurable via `Otp__OtpTtlSeconds`; see [Configuration Guide](configuration.md))

**Q: Can I use this in production?**  
A: In-memory mode is for development only. Use Azure resources for production.

**Q: Where are messages stored?**  
A: In in-memory mode, messages, users, dispatch centers, and escalations are stored in application memory and lost on restart. With Azure mode, they are persisted.

---

**Next**: [Full Installation Guide](installation.md) | [Local Setup](../development/local-setup.md) | [Back to docs](../README.md)
