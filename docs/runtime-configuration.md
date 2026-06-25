# Runtime Configuration

Project security decision: current development credentials intentionally remain in tracked `appsettings.Development.json`. Do not move those development values to user-secrets or environment variables as part of this implementation plan, do not rotate them, and do not change existing connection strings or service endpoints unless a separate operational task explicitly asks for that.

All logs, test output, reports, exceptions, and documentation must continue to mask secret values. Report only field names and configured/missing status.

Secret-bearing field names:

- `ConnectionStrings__DefaultConnection`
- `Jwt__SigningKey`
- `Email__Smtp__Password` when SMTP delivery is enabled
- `CloudinaryStorage__CloudinaryUrl` when Cloudinary storage is enabled

Example PowerShell variable names (values intentionally omitted):

```powershell
$env:ConnectionStrings__DefaultConnection = '<sql-server-connection-string>'
$env:Jwt__SigningKey = '<high-entropy-signing-key>'
$env:Email__Smtp__Password = '<smtp-secret>'
$env:CloudinaryStorage__CloudinaryUrl = '<cloudinary-secret-url>'
```

The API validates database, JWT, and enabled storage configuration during startup and should fail closed when required values are absent. Development credentials are intentionally configured in the development settings file for this project; production deployment policy is a separate operational decision.
