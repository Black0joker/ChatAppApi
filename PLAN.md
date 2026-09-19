# Real-Time Chat Backend

## 1. Project Overview

Build a production-oriented real-time chat backend using:

- **ASP.NET Core 10 Web API**
- **SignalR** for real-time communication
- **SQL Server** as the primary database
- **Entity Framework Core** for data access
- **Redis** for distributed state and SignalR backplane
- **JWT Bearer Authentication**
- **Object/File Storage** for attachments
- **Background services** for notifications and asynchronous processing

The system must support:

- One-to-one conversations
- Group conversations
- Online/offline presence
- Typing indicators
- Message history
- Read receipts
- File/image attachments
- Real-time notifications
- Multiple API instances
- Horizontal scaling

---

# 2. Target Architecture

```text
                         ┌─────────────────┐
                         │     Client A    │
                         └────────┬────────┘
                                  │
                                  │ WebSocket
                                  ▼
                         ┌─────────────────┐
                         │   Load Balancer │
                         └────────┬────────┘
                                  │
                    ┌─────────────┴─────────────┐
                    │                           │
                    ▼                           ▼
             ┌──────────────┐            ┌──────────────┐
             │  API Server A│            │  API Server B│
             │ ASP.NET Core │            │ ASP.NET Core │
             └──────┬───────┘            └──────┬───────┘
                    │                           │
                    │ SignalR                   │ SignalR
                    │                           │
                    └───────────┬───────────────┘
                                │
                                ▼
                         ┌─────────────┐
                         │    Redis    │
                         │             │
                         │ Backplane   │
                         │ Pub/Sub     │
                         │ Presence    │
                         └─────────────┘

             ┌─────────────────────────────┐
             │          SQL Server        │
             │                             │
             │ Users                       │
             │ Conversations               │
             │ Members                     │
             │ Messages                    │
             │ Attachments                  │
             │ ReadReceipts                │
             └─────────────────────────────┘
```

---

# 3. Solution Structure

Use a layered architecture rather than putting everything inside the API project.

```text
ChatApp/
│
├── src/
│   ├── ChatApp.Api/
│   │   ├── Controllers/
│   │   ├── Hubs/
│   │   ├── Middleware/
│   │   ├── Extensions/
│   │   ├── Filters/
│   │   └── Program.cs
│   │
│   ├── ChatApp.Application/
│   │   ├── Abstractions/
│   │   ├── Conversations/
│   │   ├── Messages/
│   │   ├── Users/
│   │   ├── Attachments/
│   │   ├── Notifications/
│   │   └── Common/
│   │
│   ├── ChatApp.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── ValueObjects/
│   │   ├── Events/
│   │   └── Exceptions/
│   │
│   └── ChatApp.Infrastructure/
│       ├── Persistence/
│       ├── Redis/
│       ├── Storage/
│       ├── Notifications/
│       └── Services/
│
└── tests/
    ├── ChatApp.UnitTests/
    ├── ChatApp.IntegrationTests/
    └── ChatApp.ArchitectureTests/
```

---

# 4. Main Components

## API Layer

Responsible for:

- Authentication endpoints
- Conversation endpoints
- Message history endpoints
- Attachment endpoints
- Group management
- Read receipt APIs
- Notification APIs

SignalR Hub handles real-time operations.

---

## Application Layer

Contains business use cases.

Examples:

```text
SendMessageCommand
CreateConversationCommand
CreateGroupCommand
AddGroupMemberCommand
RemoveGroupMemberCommand
MarkMessageAsReadCommand
UploadAttachmentCommand
```

The Application layer should not depend directly on SQL Server or Redis.

Use interfaces such as:

```text
IChatRepository
IConversationRepository
IMessageRepository
IPresenceService
INotificationService
IFileStorage
```

---

# 5. Domain Model

Initial entities:

```text
User
Conversation
ConversationMember
Message
MessageAttachment
MessageReadReceipt
Notification
```

Potential later entities:

```text
UserDevice
RefreshToken
BlockedUser
MessageReaction
MessageEdit
MessageDeletion
Group
```

---

# 6. Database Design

Use SQL Server as the source of truth for persistent data.

## Users

```text
Users
-----
Id
Username
DisplayName
Email
AvatarUrl
CreatedAt
LastSeenAt
```

---

## Conversations

```text
Conversations
-------------
Id
Type
Name
CreatedAt
CreatedBy
LastMessageId
```

`Type`:

```text
Direct
Group
```

---

## ConversationMembers

```text
ConversationMembers
------------------
ConversationId
UserId
Role
JoinedAt
LeftAt
LastReadMessageId
```

For group conversations:

```text
Owner
Admin
Member
```

---

## Messages

```text
Messages
--------
Id
ConversationId
SenderId
Content
MessageType
CreatedAt
EditedAt
DeletedAt
ReplyToMessageId
```

Use a database-generated or application-generated unique ID.

For distributed systems, a GUID/UUID is convenient because different API instances can generate IDs independently.

---

## MessageAttachments

```text
MessageAttachments
------------------
Id
MessageId
FileName
ContentType
Size
StorageKey
Url
CreatedAt
```

Do not store large files directly in SQL Server unless there is a specific reason to do so.

Store the file in object/file storage and keep metadata in SQL Server.

---

## MessageReadReceipts

```text
MessageReadReceipts
-------------------
MessageId
UserId
ReadAt
```

For group conversations, this allows each member to have an independent read state.

---

# 7. Database Indexes

Plan indexes early because message history will become one of the largest tables.

Important indexes:

```text
Messages
--------
IX_Messages_ConversationId_CreatedAt
IX_Messages_SenderId_CreatedAt

ConversationMembers
------------------
IX_ConversationMembers_UserId
IX_ConversationMembers_ConversationId

MessageReadReceipts
-------------------
IX_MessageReadReceipts_UserId_MessageId
```

The most important query should be optimized:

```text
Get messages for conversation ordered by CreatedAt
```

Use cursor/keyset pagination rather than large `OFFSET` pagination.

Example:

```text
GET /api/conversations/{conversationId}/messages
    ?before={messageId}
    &limit=50
```

---

# 8. Authentication

Use JWT Bearer authentication.

Flow:

```text
Client
   │
   │ Login
   ▼
POST /api/auth/login
   │
   ▼
JWT Access Token
   │
   ├──────────────► REST API
   │
   └──────────────► SignalR Hub
```

SignalR connections must authenticate the user.

The authenticated user should be available through:

```csharp
Context.User
```

Never trust a client-provided:

```text
senderId
userId
```

The server should derive the current user from the authenticated identity.

---

# 9. SignalR Hub

Create:

```text
ChatHub
```

Example responsibilities:

```text
Connect
Disconnect

SendMessage
TypingStarted
TypingStopped
MarkAsRead

JoinConversation
LeaveConversation
```

Keep the Hub thin.

Do not put database/business logic directly inside the Hub.

Example architecture:

```text
ChatHub
   │
   ▼
Application Service
   │
   ├── Repository
   ├── Redis
   └── Notification Service
```

---

# 10. SignalR Groups

Use SignalR Groups to represent active conversation channels.

Example:

```text
conversation:{conversationId}
```

For example:

```text
conversation:8e7c...
```

When a client opens a conversation:

```text
JoinConversation(conversationId)
```

Server verifies that the user is actually a member of the conversation.

Then:

```csharp
await Groups.AddToGroupAsync(
    Context.ConnectionId,
    $"conversation:{conversationId}");
```

Never allow arbitrary clients to join arbitrary groups without authorization.

---

# 11. One-to-One Chat

For direct conversations:

```text
User A
  │
  ▼
Conversation
  │
  ├── User A
  └── User B
```

Messages are persisted:

```text
Messages
   │
   ▼
SQL Server
```

Then broadcast:

```text
SignalR Group
   │
   ├── User A
   └── User B
```

Example event:

```text
MessageReceived
```

Payload:

```json
{
  "messageId": "...",
  "conversationId": "...",
  "senderId": "...",
  "content": "Hello",
  "createdAt": "..."
}
```

---

# 12. Group Chat

Group creation:

```text
POST /api/conversations/groups
```

Creates:

```text
Conversation
+
ConversationMembers
```

SignalR group:

```text
conversation:{id}
```

When a message is sent:

```text
Sender
   │
   ▼
ChatHub
   │
   ▼
Application Service
   │
   ├── Save message
   │
   └── Publish event
          │
          ▼
       SignalR Group
          │
          ├── Member A
          ├── Member B
          ├── Member C
          └── Member D
```

---

# 13. Online / Offline Presence

Presence is different from persistent user data.

Do not use SQL Server for every connection/disconnection event.

Use Redis for distributed presence.

Example:

```text
presence:user:{userId}
```

Possible value:

```text
online
```

Or maintain connection information:

```text
presence:user:{userId}:connections
```

Conceptually:

```text
User
 │
 ├── Connection A
 ├── Connection B
 └── Connection C
```

The user should be considered online while at least one active connection exists.

This matters because a user may have:

```text
Phone
Browser
Desktop
```

connected simultaneously.

---

# 14. Presence With Multiple API Instances

Without Redis:

```text
Server A
   └── User A online

Server B
   └── doesn't know
```

With Redis:

```text
Server A
     │
     ▼
   Redis
     ▲
     │
Server B
```

Both instances can observe distributed presence state.

Recommended approach:

```text
Connect
   │
   ▼
Register connection in Redis
   │
   ▼
If first connection:
   │
   ▼
Publish UserOnline
```

On disconnect:

```text
Disconnect
   │
   ▼
Remove connection from Redis
   │
   ▼
If no connections remain:
   │
   ▼
Publish UserOffline
```

Use a TTL/heartbeat strategy so abandoned connections do not leave stale presence data.

---

# 15. Typing Indicators

Typing state is temporary.

Do **not** persist every typing event to SQL Server.

Flow:

```text
Client A
   │
   │ TypingStarted
   ▼
SignalR
   │
   ▼
Conversation Group
   │
   ▼
Client B
```

Events:

```text
TypingStarted
TypingStopped
```

Payload:

```json
{
  "conversationId": "...",
  "userId": "..."
}
```

Typing indicators should normally be ephemeral.

Redis may be used if typing state must be coordinated across multiple instances, but avoid creating unnecessary persistent state.

---

# 16. Message Sending Flow

Recommended flow:

```text
Client
  │
  │ SendMessage
  ▼
ChatHub
  │
  ▼
Validate authentication
  │
  ▼
Validate conversation membership
  │
  ▼
Application Service
  │
  ▼
SQL Server
  │
  │ Save message
  ▼
Message created
  │
  ▼
Publish/broadcast event
  │
  ▼
SignalR
  │
  ▼
Conversation members
```

Important:

**Persist the message before treating the message as successfully accepted.**

The database is the durable source of truth.

---

# 17. Read Receipts

When a user reads messages:

```text
Client
   │
   │ MarkAsRead
   ▼
SignalR
   │
   ▼
Application Service
   │
   ▼
SQL Server
```

Then broadcast:

```text
MessageRead
```

Example:

```json
{
  "conversationId": "...",
  "messageId": "...",
  "userId": "...",
  "readAt": "..."
}
```

For one-to-one chat this can represent:

```text
Sent
Delivered
Read
```

For groups, track the read state per member.

---

# 18. Attachments

Recommended upload flow:

```text
Client
   │
   │ Upload
   ▼
POST /api/attachments
   │
   ▼
File Storage
   │
   ▼
Storage Key
```

Then:

```text
SendMessage
   │
   └── attachmentId
```

Server verifies that the attachment belongs to the authenticated user before associating it with a message.

Store metadata in SQL Server:

```text
MessageAttachment
```

Do not trust:

```text
Content-Type
File extension
File size
```

provided by the client.

Validate uploads server-side.

---

# 19. Notifications

Separate real-time delivery from durable notifications.

Example:

```text
Message Created
      │
      ├──────────────► SignalR
      │
      └──────────────► Notification Service
                              │
                              ▼
                       Push Notification
```

Potential notification channels:

```text
SignalR
Push Notification
Email
```

For mobile push notifications, introduce a provider abstraction:

```text
IPushNotificationService
```

This keeps the application independent of the specific provider.

---

# 20. Redis Responsibilities

Redis should not become a second SQL Server.

Use SQL Server for:

```text
Users
Messages
Conversations
Membership
Attachments metadata
Read receipts
Notifications
```

Use Redis for things such as:

```text
SignalR backplane
Distributed presence
Ephemeral state
Caching
Rate limiting
Distributed locks where necessary
```

---

# 21. SignalR Redis Backplane

With multiple API instances:

```text
                  ┌──────────────┐
Client A ────────►│   Server A   │
                  └──────┬───────┘
                         │
                         ▼
                      Redis
                         ▲
                         │
                  ┌──────┴───────┐
Client B ────────►│   Server B   │
                  └──────────────┘
```

Example scenario:

```text
Client A → Server A
Client B → Server B
```

When Server A sends a SignalR group message, Redis allows the SignalR infrastructure to propagate the message so clients connected through other instances can receive it.

This is one of the primary reasons Redis becomes important once the application is horizontally scaled.

---

# 22. Redis Is Not the Message Database

Avoid this design:

```text
Message
   │
   ▼
Redis
```

as the permanent source of messages.

Prefer:

```text
Message
   │
   ▼
SQL Server
   │
   └── durable history

Redis
   │
   └── distributed real-time infrastructure
```

If Redis becomes unavailable, historical chat data should still exist in SQL Server.

---

# 23. Message Ordering

Distributed systems make message ordering important.

Each message should have:

```text
MessageId
CreatedAt
```

For stronger ordering guarantees, consider a monotonically sortable identifier or a server/database-generated sequence appropriate to your design.

Never rely exclusively on client timestamps for ordering.

The server determines message creation time.

---

# 24. Idempotency

Clients can retry requests because of:

```text
Network failure
Reconnect
Timeout
Mobile connection changes
```

Therefore, sending a message should support an idempotency key/client message ID.

Example:

```json
{
  "clientMessageId": "...",
  "conversationId": "...",
  "content": "Hello"
}
```

Store a unique constraint such as:

```text
SenderId + ClientMessageId
```

This prevents:

```text
Client retries
      │
      ├── Message #1
      └── Message #1 again
```

from creating duplicate messages.

---

# 25. Reconnection Strategy

SignalR clients can disconnect and reconnect.

The client should:

```text
Connect
   │
   ▼
Authenticate
   │
   ▼
Join required conversation groups
   │
   ▼
Request missed messages
```

Do not assume that SignalR reconnection automatically means the client has received every event it missed.

Use SQL Server message history as the recovery mechanism.

---

# 26. Message Synchronization

When reconnecting:

```text
Client
   │
   │ Last known message
   ▼
GET messages after/before cursor
   │
   ▼
SQL Server
```

Example:

```text
GET /api/conversations/{id}/messages?after={messageId}
```

This gives the client a durable synchronization mechanism.

---

# 27. API Endpoints

Initial API surface:

## Authentication

```text
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout
```

## Users

```text
GET /api/users/me
GET /api/users/{id}
GET /api/users/search
```

## Conversations

```text
POST /api/conversations/direct
POST /api/conversations/groups

GET /api/conversations
GET /api/conversations/{id}

POST /api/conversations/{id}/members
DELETE /api/conversations/{id}/members/{userId}
```

## Messages

```text
GET /api/conversations/{id}/messages

DELETE /api/messages/{id}

PUT /api/messages/{id}
```

## Attachments

```text
POST /api/attachments
GET /api/attachments/{id}
DELETE /api/attachments/{id}
```

## Notifications

```text
GET /api/notifications
POST /api/notifications/{id}/read
```

---

# 28. SignalR Contract

Initial hub methods:

```text
SendMessage
TypingStarted
TypingStopped
MarkAsRead
JoinConversation
LeaveConversation
```

Server-to-client events:

```text
MessageReceived
MessageUpdated
MessageDeleted

TypingStarted
TypingStopped

UserOnline
UserOffline

MessageRead

NotificationReceived
```

Keep these contracts versionable.

---

# 29. Error Handling

Use consistent API errors.

Recommended format:

```json
{
  "type": "https://example.com/errors/conversation-not-found",
  "title": "Conversation not found",
  "status": 404,
  "detail": "The requested conversation does not exist.",
  "traceId": "..."
}
```

For SignalR, define a consistent error strategy rather than exposing internal exceptions.

Never send:

```text
SQL exception
stack trace
internal Redis error
connection string
```

to clients.

---

# 30. Validation

Validate:

```text
Message length
Conversation membership
Group permissions
Attachment size
Attachment type
User permissions
Rate limits
```

Example:

```text
Maximum message size
Maximum attachment size
Maximum group members
Maximum requests/second
```

These limits should be configurable.

---

# 31. Authorization

Authentication answers:

```text
Who are you?
```

Authorization answers:

```text
Are you allowed to perform this operation?
```

Every conversation operation should verify membership.

Example:

```text
User A
  │
  └── Conversation 123 ✓

User B
  │
  └── Conversation 123 ✗
```

User B must not be able to:

```text
Read messages
Send messages
Join SignalR group
Modify members
Delete messages
```

unless explicitly authorized.

---

# 32. Observability

Add structured logging from the beginning.

Log useful identifiers:

```text
TraceId
UserId
ConversationId
MessageId
ConnectionId
```

Avoid logging message content by default because chat content can be sensitive.

Add metrics such as:

```text
Active SignalR connections
Messages/second
Message send latency
Database latency
Redis latency
Connection failures
Reconnect count
Attachment upload failures
Notification failures
```

---

# 33. Health Checks

Expose:

```text
/health
/health/ready
```

Check dependencies such as:

```text
SQL Server
Redis
File Storage
```

Distinguish:

```text
Liveness
Readiness
```

so a temporarily unavailable dependency can be handled correctly by orchestration/load balancing.

---

# 34. Testing Strategy

## Unit Tests

Test:

```text
Message validation
Conversation authorization
Group permissions
Read receipt rules
Attachment rules
Message creation
```

---

## Integration Tests

Test against real infrastructure where practical:

```text
SQL Server
Redis
ASP.NET Core
SignalR
```

Test scenarios:

```text
Send message
Read history
Create group
Add member
Remove member
Read receipt
Presence
```

---

## Multi-Instance Tests

This is particularly important.

Run:

```text
Server A
Server B
Redis
SQL Server
```

Then test:

```text
Client A → Server A
Client B → Server B
```

and verify:

```text
Client A sends message
        ↓
Server A
        ↓
Redis / SignalR backplane
        ↓
Server B
        ↓
Client B receives message
```

Also test presence and reconnection across instances.

---

# 35. Local Development Infrastructure

Use Docker Compose for:

```text
SQL Server
Redis
```

Development environment:

```text
docker-compose.yml

services:

  sqlserver:
    image: ...
    
  redis:
    image: ...
```

The API can initially run directly from the IDE/CLI.

Later, containerize the API as well.

---

# 36. Configuration

Use configuration sections:

```json
{
  "ConnectionStrings": {
    "SqlServer": "...",
    "Redis": "..."
  },

  "Jwt": {
    "Issuer": "...",
    "Audience": "...",
    "SigningKey": "..."
  },

  "Storage": {
    "Provider": "...",
    "Bucket": "..."
  },

  "Chat": {
    "MaxMessageLength": 4000,
    "MaxAttachmentSize": 10485760
  }
}
```

Never commit secrets to source control.

Use environment variables or a secret-management solution.

---

# 37. EF Core Strategy

Use:

```text
Code First
```

with migrations.

Example:

```text
Add-Migration InitialCreate
Update-Database
```

Keep migrations in the Infrastructure project.

Use separate configurations:

```text
UserConfiguration
ConversationConfiguration
MessageConfiguration
ConversationMemberConfiguration
```

rather than putting all EF configuration inside entity classes.

---

# 38. Transaction Boundaries

For sending a message:

```text
Begin transaction

Create Message
Create Attachment relationships
Update Conversation.LastMessageId

Commit
```

After successful persistence, publish/broadcast the event.

Avoid publishing a real-time message before the database transaction succeeds.

---

# 39. Reliable Event Publishing

As the system grows, consider the **Outbox Pattern**.

Instead of:

```text
SQL Server
   │
   └── Save message

Redis
   │
   └── Publish event
```

use:

```text
SQL Transaction
   │
   ├── Message
   │
   └── OutboxEvent
          │
          ▼
      Background Worker
          │
          ▼
        Redis
          │
          ▼
       SignalR
```

This prevents situations where:

```text
Message saved successfully
BUT
event publishing failed
```

The Outbox Pattern should be introduced once the basic chat flow is working and tested.

---

# 40. Recommended Development Phases

## Phase 1 — Project Foundation

Build:

```text
ASP.NET Core 10
Solution structure
EF Core
SQL Server
Docker Compose
Configuration
Logging
Health checks
```

Deliverable:

```text
API successfully connects to SQL Server.
```

---

## Phase 2 — Authentication

Implement:

```text
Register
Login
JWT
Refresh token
Current user
Authorization
```

Deliverable:

```text
Authenticated API + authenticated SignalR connection.
```

---

## Phase 3 — Conversations

Implement:

```text
Direct conversation
Group conversation
Members
Roles
Authorization
```

Deliverable:

```text
Users can create and access conversations.
```

---

## Phase 4 — Message Persistence

Implement:

```text
Message entity
Message repository
Send message
Message history
Pagination
Indexes
```

Deliverable:

```text
Messages are durably stored in SQL Server.
```

---

## Phase 5 — SignalR

Implement:

```text
ChatHub
SendMessage
MessageReceived
JoinConversation
LeaveConversation
```

Deliverable:

```text
Two connected clients can exchange messages in real time.
```

---

## Phase 6 — Redis

Add:

```text
Redis
SignalR Redis backplane
Distributed presence
```

Then run:

```text
Server A
Server B
```

Deliverable:

```text
Clients connected to different API instances can communicate in real time.
```

---

## Phase 7 — Presence

Implement:

```text
Online
Offline
LastSeen
Multiple connections per user
Heartbeat/TTL
```

Deliverable:

```text
Presence works across multiple API instances.
```

---

## Phase 8 — Typing Indicators

Implement:

```text
TypingStarted
TypingStopped
```

Keep the state ephemeral.

Deliverable:

```text
Typing indicators work without database writes.
```

---

## Phase 9 — Read Receipts

Implement:

```text
MarkAsRead
ReadReceipt
MessageRead
```

Deliverable:

```text
Users can see when messages have been read.
```

---

## Phase 10 — Attachments

Implement:

```text
Upload
Validation
Storage
Attachment metadata
Message association
```

Deliverable:

```text
Users can send files/images with messages.
```

---

## Phase 11 — Notifications

Implement:

```text
Notification entity
Notification service
Real-time notification
Push notification abstraction
```

Deliverable:

```text
Offline users can receive appropriate notifications.
```

---

## Phase 12 — Reliability

Add:

```text
Idempotency
Outbox Pattern
Retry policies
Reconnect synchronization
Distributed tracing
Metrics
```

Deliverable:

```text
System behaves predictably under retries, disconnects, and failures.
```

---

## Phase 13 — Testing

Run:

```text
Unit tests
Integration tests
SignalR tests
Redis tests
Multi-instance tests
Load tests
```

Especially test:

```text
Server A ↔ Redis ↔ Server B
```

Deliverable:

```text
Scalable multi-instance chat backend.
```

---

# 41. Final Architecture

The target system should eventually look like:

```text
                         ┌─────────────────┐
                         │     Clients     │
                         │                 │
                         │ Web / Mobile    │
                         └────────┬────────┘
                                  │
                           HTTPS / WebSocket
                                  │
                                  ▼
                         ┌─────────────────┐
                         │ Load Balancer   │
                         └────────┬────────┘
                                  │
                    ┌─────────────┴─────────────┐
                    │                           │
                    ▼                           ▼
             ┌──────────────┐            ┌──────────────┐
             │ ASP.NET Core │            │ ASP.NET Core │
             │   Server A   │            │   Server B   │
             └──────┬───────┘            └──────┬───────┘
                    │                           │
                    └────────────┬──────────────┘
                                 │
                         ┌───────▼────────┐
                         │     Redis      │
                         │                │
                         │ SignalR         │
                         │ Backplane       │
                         │ Pub/Sub         │
                         │ Presence        │
                         │ Cache           │
                         └────────────────┘

             ┌──────────────────────────────────┐
             │            SQL Server            │
             │                                  │
             │ Users                            │
             │ Conversations                    │
             │ Members                          │
             │ Messages                         │
             │ Attachments                      │
             │ Read Receipts                    │
             │ Notifications                    │
             │ Outbox                           │
             └──────────────────────────────────┘

             ┌──────────────────────────────────┐
             │         File/Object Storage      │
             │                                  │
             │ Images                           │
             │ Videos                           │
             │ Documents                        │
             └──────────────────────────────────┘
```

---

# 42. Important Design Rules

Keep these rules throughout the project:

1. **SQL Server is the source of truth for persistent chat data.**
2. **Redis is for distributed/ephemeral infrastructure, not permanent message history.**
3. **SignalR Hubs should remain thin.**
4. **Business logic belongs in Application services.**
5. **Never trust `userId` or `senderId` supplied by the client.**
6. **Authorize conversation membership on every sensitive operation.**
7. **Persist messages before broadcasting them as successfully accepted.**
8. **Use idempotency to prevent duplicate messages.**
9. **Use cursor-based pagination for message history.**
10. **Treat SignalR reconnection as a synchronization event.**
11. **Support multiple connections per user.**
12. **Do not write typing events to SQL Server.**
13. **Use Redis for cross-instance coordination.**
14. **Introduce the Outbox Pattern when reliable event delivery becomes necessary.**
15. **Test with at least two API instances before calling the distributed architecture complete.**

---

# 43. First Milestone

Do not implement every feature at once.

The first working milestone should be:

```text
ASP.NET Core 10
      │
      ├── JWT Authentication
      │
      ├── SQL Server
      │
      ├── EF Core
      │
      ├── Conversation
      │
      ├── Message
      │
      └── SignalR
             │
             ▼
       Real-time chat
```

Then introduce Redis:

```text
ASP.NET Core A ──┐
                 ├── Redis ──► distributed SignalR
ASP.NET Core B ──┘
```

Once that works, add:

```text
Presence
   ↓
Typing
   ↓
Read receipts
   ↓
Attachments
   ↓
Notifications
   ↓
Outbox / reliability
   ↓
Load testing
```

This keeps the project incremental and makes it much easier to understand **exactly why Redis is needed when the application moves from one API instance to multiple instances**.