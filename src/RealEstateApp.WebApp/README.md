# RealEstate MVC

Presentation layer for .NET 10. Controllers depend only on Application interfaces,
AutoMapper, and ASP.NET MVC. Infrastructure is referenced only by the composition
root in `Program.cs`. Domain usage is limited to roles, enums, and exceptions.

## Run

```powershell
dotnet run --project src/RealEstateApp.WebApp --launch-profile https
```

The launch profile exposes `https://localhost:7241` and `http://localhost:5141`.
Identity cookies require HTTPS. The local .NET development certificate must be trusted.
The default database is SQL Server LocalDB, database `RealEstateApp`.
Override `ConnectionStrings__DefaultConnection` when using a different SQL Server.

Set `PublicUrl` to the trusted HTTPS base URL of the deployed MVC application,
including a path base if applicable. MVC appends `/Account/Confirm` and passes this
complete confirmation endpoint to `IAccountService.RegisterAsync`. It never builds
activation links from the incoming Host header. The account service owns token
generation, encoding, delivery, and activation. Configure the Infrastructure email
sender for delivery. No API URL or JWT signing secret is required by MVC.

User seeding and database initialization belong to
`InitializeInfrastructureAsync(configuration)`. Configure Infrastructure's seed
settings externally. Do not commit seed passwords, SMTP credentials, or signing keys.
`AllowedHosts` must also be configured for the deployment's hostname.

## Routes

| Section | Entry Points |
| --- | --- |
| Public | `/Home/Index`, `/Home/Agents`, `/Home/AgentProperties/{id}`, `/Home/Details/{id}` |
| Accounts | `/Account/Register`, `/Account/Login`, `/Account/Confirm?userId=...&token=...`, `/Account/AccessDenied` |
| Client | `/Client/Index`, `/Client/Favorites`, `/Client/Details/{id}` |
| Agent | `/Agent/Index`, `/Agent/Maintenance`, `/Agent/Create`, `/Agent/Edit/{id}`, `/Agent/Delete/{id}`, `/Agent/Profile`, `/Agent/Details/{id}?clientId=...` |
| Administration | `/Admin/Index`, `/Admin/Agents` |
| User Maintenance | `/AdminUsers/Index?role=Administrador`, `/AdminUsers/Index?role=Desarrollador` |
| Catalogs | `/Catalogs/Index?kind=PropertyTypes`, `SaleTypes`, or `Improvements` |

All property listings reuse `_PropertyFilters` with the `Filter.*` query prefix.
Only Application determines ownership and visibility through `public`, `favorites`,
`agent`, and `maintenance` search scopes. Detail reads use `public` or `agent`;
maintenance forms also verify availability before showing edit/delete controls.
Codes, owners, activation state, and property status are never editable form fields.

All writes use POST with a global antiforgery filter, including logout, favorites,
status changes, offer responses, and deletion confirmations. The sole intentional
GET mutation is the token-bearing email confirmation endpoint. Return URLs are
accepted only after `Url.IsLocalUrl` validation. Razor encodes all user content.
The exception filter renders friendly 400/401/403/404/500 pages; unexpected details
are logged server-side, never included in the page. GET confirmation screens do
not mutate data.

## Verification Checklist

1. Build the WebApp after Application and Infrastructure are available. Razor views
   are compiled with the application. No JavaScript build or npm install is needed.
2. Public: verify newest-first listing, all six combined filters, reset, invalid
   ranges, empty results, gallery, agent name/surname search, inactive-agent exclusion.
3. Accounts: required fields, JPG/PNG upload validation, client email activation,
   agent pending activation, invalid/expired tokens, duplicate email/user, and a
   malicious external login return URL. Developer login must not create a WebApp session.
4. Client: add/remove favorite explicitly, filtered favorites, own chat and offer
   history only, empty message rejection, positive offers, pending-offer lockout.
5. Agent: own available/sold listings, available-only maintenance, create/edit with
   1-4 total retained/new images, tampered retained IDs, immutable code/owner,
   delete cancellation, profile photo retention, selected-client threads and offers.
6. Accept an offer, then verify sold label, other offers rejected by Application,
   read-only histories, and no edit/delete/message/offer allowed for the sold property.
7. Administrator: eight real counts, agent status/delete confirmations, exact role
   restrictions, user create/edit/optional password, self-edit/disable rejected by
   Application, catalog counts and deletion consequences. Improvement deletion must
   preserve properties; property/sale type deletion removes associated properties.
8. Security: no antiforgery token on POST returns 400; GET logout or mutation URLs
   cannot write; private routes redirect anonymous visitors to login and reject
   another role. Test HTML/script text in chat, names, and descriptions renders as text.
9. Desktop/mobile: keyboard navigation, labels and summaries, horizontal table scroll,
   and essential rendering with the Bootstrap CDN blocked and JavaScript disabled.

The CSS supplies the complete essential layout without the optional Bootstrap CDN.
Gallery and upload previews are progressive enhancements. Without JavaScript,
thumbnail links open the image and all forms still submit normally. All money is
displayed explicitly in DOP and timestamps use Dominican Republic time (UTC-4).
