# CLAUDE.md

Guidance for Claude Code when working in this repo.

## What this is

A single .NET 8 AWS Lambda (API Gateway proxy integration) that receives form
submissions from portfolio websites, emails them to the right recipient via Amazon SES,
and records them in DynamoDB. No web framework: one handler, a few plain classes.

Handler: `PortfolioContactApi::PortfolioContactApi.Function::FunctionHandler`

## Commands

```bash
dotnet build                      # build
dotnet publish -c Release         # release build (ReadyToRun enabled)
dotnet lambda deploy-function     # manual deploy (needs Amazon.Lambda.Tools + AWS creds)
```

- There is no test project (tests were deleted in an earlier commit). Verify changes with
  `dotnet build` at minimum.
- Pushing to `main` deploys to production via `.github/workflows/deploy.yml`
  (us-east-1, role `PortfolioContactLambdaRole`). Treat merges to `main` as releases.

## Layout

| Path | Purpose |
|---|---|
| `Function.cs` | Handler: CORS, OPTIONS preflight, IP rate limit, API key, origin check, deserialize, validate, client lookup, email rate limit, SES send, store submission |
| `RateLimiter.cs` | DynamoDB sliding-window limits: `ip:<ip>` per hour, `email:<clientId>` per day |
| `SubmissionStore.cs` | Writes every successful submission to DynamoDB (`submission:<guid>`) |
| `Models/IFormRequest.cs` | Contract every form type implements (`ClientId`, `GetFormType()`, `GetFormData()`) |
| `Models/ContactRequest.cs` | `contact` form: name, email, message |
| `Models/MyFolioVisionContactRequest.cs` | `vision` form: project inquiry fields |
| `Services/FormDeserializer.cs` | Maps the `x-form-type` header to a model type |
| `Services/EmailFormatter.cs` | Builds subject, text and HTML bodies from `GetFormData()` |

## Request flow (order matters)

1. OPTIONS returns 200 with CORS headers.
2. IP rate limit (429), then API key from `x-api-key` (403), then origin allowlist (403).
3. `x-form-type` header picks the model (default `contact`). Unknown type returns 400.
4. Every value in `GetFormData()` must be non-empty (400). `clientId` must exist in
   `CLIENT_EMAIL_MAPPINGS` (400).
5. Per-client daily email limit (429), SES send, then store submission. Storage failure
   does not fail the request.

## Adding a new form type

1. Add a model in `Models/` implementing `IFormRequest`. Every key in `GetFormData()`
   is treated as required and becomes a `field_<key>` attribute in DynamoDB.
2. Register it in the `switch` in `Services/FormDeserializer.cs`.
3. Add a subject line in `Services/EmailFormatter.cs`.
4. The reply-to address is the first form field whose key contains "Email". Fields with
   "Message" or "Description" in the key render as multi-line blocks in the HTML email.

## Configuration (Lambda environment variables)

| Variable | Notes |
|---|---|
| `API_KEY` | Required. If empty, every request is rejected with 403 |
| `VERIFIED_SENDER_EMAIL` | SES-verified `Source` address |
| `CLIENT_EMAIL_MAPPINGS` | `clientA:a@x.com,clientB:b@y.com`. Duplicate IDs throw at cold start |
| `ALLOWED_ORIGINS` | Comma-separated origins |
| `RATE_LIMIT_TABLE` | DynamoDB table, partition key `pk` (S), TTL on `ttl`. Empty disables rate limiting |
| `MAX_REQUESTS_PER_HOUR` | Per IP, default 10 |
| `MAX_EMAILS_PER_DAY` | Per client ID, default 20 |
| `SUBMISSION_STORE_TABLE` | DynamoDB table, partition key `pk` (S). Empty disables storage. `GetSubmissionsByClientAsync` needs a GSI `clientId-timestamp-index` |

Never commit real values for any of these.

## Conventions

- Namespaces: root classes use `PortfolioContactApi` (block-scoped), `Models/` and
  `Services/` use file-scoped `PortfolioContactApi.Models` / `.Services`.
- Nullable reference types and implicit usings are on.
- Dependencies are injected through the second `Function` constructor; the
  parameterless one wires real AWS clients from env vars. Keep that pattern so the
  handler stays testable.
- Logging goes through `context.Logger` in the handler and `Console.WriteLine` elsewhere
  (both land in CloudWatch).
- Commit messages use Conventional Commits (`feat:`, `fix:`).

## Known gaps (check before relying on the README)

- `Readme.md` is out of date: it doesn't cover rate limiting, DynamoDB storage, form
  types or the `x-form-type` header, lists the wrong project structure and deploy path,
  and documents length limits that the code doesn't enforce.
- `Function.cs` logs the full request body (PII) to CloudWatch.
- The origin check uses substring `Contains`, not an exact match.
- The API key comparison is not constant-time.
- `RateLimiter` fails open on DynamoDB errors and uses read-then-write (not atomic).
- The deploy workflow sets 256 MB memory; `aws-lambda-tools-defaults.json` says 512 MB.
