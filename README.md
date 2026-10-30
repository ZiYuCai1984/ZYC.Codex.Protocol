<h1 align="center">ZYC.Codex.Protocol</h1>

<p align="center">
  <a href="https://www.nuget.org/packages/ZYC.Codex.Protocol">
    <img src="https://img.shields.io/nuget/vpre/ZYC.Codex.Protocol?logo=nuget" alt="NuGet Version (including prereleases)" />
  </a>
  <a href="https://www.nuget.org/packages/ZYC.Codex.Protocol">
    <img src="https://img.shields.io/nuget/dt/ZYC.Codex.Protocol?logo=nuget&amp;label=Downloads" alt="NuGet Downloads" />
  </a>
  <img src="https://img.shields.io/badge/.NET_Standard-2.0-512BD4?logo=dotnet&amp;logoColor=white" alt=".NET Standard 2.0" />
  <a href="LICENSE">
    <img src="https://img.shields.io/badge/License-MIT-green" alt="MIT License" />
  </a>
  <a href="https://github.com/ZiYuCai1984/ZYC.Codex.Protocol/actions/workflows/update-codex-protocol.yml">
    <img src="https://img.shields.io/github/actions/workflow/status/ZiYuCai1984/ZYC.Codex.Protocol/update-codex-protocol.yml?label=Protocol%20update&amp;logo=github" alt="Daily protocol update workflow" />
  </a>
</p>


.NET data contracts for the Codex app-server protocol, including requests, responses, notifications, union types, and `System.Text.Json` converters for constructing and parsing strongly typed protocol messages.

The library handles message modeling and JSON serialization. The consuming application manages the app-server process, transport, initialization handshake, request dispatch, and approval handling.

## Installation

Install [ZYC.Codex.Protocol from NuGet](https://www.nuget.org/packages/ZYC.Codex.Protocol) in your application project:

```shell
dotnet add package ZYC.Codex.Protocol --prerelease
```

Import the `ZYC.Codex.Protocol` namespace to use the protocol types. JSON converters are applied through attributes on types and properties; no manual converter registration is required.

## Protocol Version

<!-- codex-version:start -->
| Component | Version |
| --- | --- |
| Codex CLI / schema | [0.166.0](https://www.npmjs.com/package/@openai/codex/v/0.166.0) |
| NuGet package | [1.0.0-0.166.0](https://www.nuget.org/packages/ZYC.Codex.Protocol/1.0.0-0.166.0) |
<!-- codex-version:end -->

Automated packages follow the Generator's `ProductInfo.Version` expression. The current `1.0.0-{CodexVersion}` template produces versions such as `1.0.0-0.164.0`. The Codex CLI version identifies the schema exported by that CLI, as described in the [app-server documentation](https://developers.openai.com/codex/app-server). `ProductInfo.Version` exposes the full package version and `ProductInfo.CodexVersion` exposes the pinned upstream version (empty for the original untracked snapshot).

The suffix is a NuGet prerelease identifier, so enable prerelease packages when installing or updating, or select the exact version from the table. The library base is maintained manually in the Generator's `ProductInfo.Version`. Build metadata such as `+codex.0.114.0` cannot distinguish package versions because [NuGet removes it during version normalization](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#normalized-version-numbers).

## Creating Requests

Create a `thread/start` request while preserving an explicitly assigned `false`:

```csharp
using System;
using System.Text.Json;
using ZYC.Codex.Protocol;

ClientRequest request = new ThreadStartRequest
{
    Id = 1L,
    Params = new ThreadStartParams
    {
        Ephemeral = false
    }
};

string json = JsonSerializer.Serialize(request);
Console.WriteLine(json);
```

Output:

```json
{"id":1,"method":"thread/start","params":{"ephemeral":false}}
```

Create a request to submit text to an existing thread:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using ZYC.Codex.Protocol;

ClientRequest request = new TurnStartRequest
{
    Id = "turn-request-1",
    Params = new TurnStartParams
    {
        ThreadId = "thread-1", // Replace with a thread ID returned by the server.
        Input = new List<UserInput>
        {
            new TextUserInput { Text = "Explain this project." }
        }
    }
};

string json = JsonSerializer.Serialize(request);
```

Fixed fields such as `Method` and `Type` are defined by the concrete type and do not need to be assigned. Supply the required data when constructing a request. These examples produce JSON; your application must establish a connection and complete initialization before sending messages.

## Parsing Messages

Deserialize through a union base type to let its converter select a concrete type based on `method`, `type`, or the message structure:

```csharp
using System;
using System.Text.Json;
using ZYC.Codex.Protocol;

const string json = "{\"id\":1,\"method\":\"thread/start\",\"params\":{}}";
ClientRequest? request = JsonSerializer.Deserialize<ClientRequest>(json);

if (request is ThreadStartRequest start)
{
    Console.WriteLine(start.Method); // thread/start
    Console.WriteLine(start.Params.Model.IsSpecified); // False
}
```

Common message types:

| Type | Purpose |
| --- | --- |
| `ClientRequest` | Requests initiated by the client, such as `ThreadStartRequest` and `TurnStartRequest` |
| `ClientNotification` | Client notifications, such as `InitializedNotification` |
| `ServerRequest` | Requests initiated by the server, such as command execution approval requests |
| `ServerNotification` | Server notifications, such as `ItemAgentMessageDeltaNotification` |
| `JSONRPCMessage` | A general message wrapper that distinguishes requests, notifications, successful responses, and error responses by structure |
| `JSONRPCResponse` / `JSONRPCError` | Successful responses / error responses |

Concrete branches of `JSONRPCMessage` expose the underlying message through `Value`, for example `JSONRPCMessageJSONRPCResponse.Value`. A successful response stores its `Result` as a `JsonElement`. Match the response ID to the original request, then deserialize the result into the corresponding response type, for example with `response.Result.Deserialize<ThreadStartResponse>()`.

You can also deserialize a concrete type directly, for example with `JsonSerializer.Deserialize<ThreadStartRequest>(json)`. Fixed discriminator fields must be present and match the target type; otherwise, deserialization throws `JsonException`. Converters also reject unknown enum values and unrecognized union cases.

## Optional Fields: Optional<T>

Optional fields use `Optional<T>` to track whether a property was provided. The payload type `T` preserves the nullability declared in the schema:

- `Optional<long>` can be omitted or provided as an integer.
- `Optional<string?>` can be omitted or explicitly provided as `null` or a string.
- Required fields use their mapped types directly, such as `string` or `int?`, without an `Optional<T>` wrapper.

For example, `ThreadMetadataGitInfoUpdateParams.Branch` supports three states:

| C# state | JSON output | Protocol meaning |
| --- | --- | --- |
| Unassigned or `Optional<string?>.Unspecified` | `{}` | Keep the stored branch unchanged |
| `Branch = null` | `{"branch":null}` | Clear the stored branch |
| `Branch = "main"` | `{"branch":"main"}` | Set the branch |

```csharp
using System;
using System.Text.Json;
using ZYC.Codex.Protocol;

var patch = new ThreadMetadataGitInfoUpdateParams();
Console.WriteLine(JsonSerializer.Serialize(patch)); // {}

patch.Branch = null;
Console.WriteLine(JsonSerializer.Serialize(patch)); // {"branch":null}

patch.Branch = "main";
Console.WriteLine(JsonSerializer.Serialize(patch)); // {"branch":"main"}

patch.Branch = Optional<string?>.Unspecified;
Console.WriteLine(JsonSerializer.Serialize(patch)); // {}
```

Check `IsSpecified` before reading a value:

```csharp
if (patch.Branch.IsSpecified)
{
    string? branch = patch.Branch.Value;
    // A null value here was explicitly provided.
}
```

Accessing `Value` when the field is unspecified throws `InvalidOperationException`. Explicit values such as `false`, `0`, and `null` are preserved; only unspecified properties are omitted. Deserialization preserves the same distinction.

Omission applies to object properties. Serializing `Optional<T>.Unspecified` directly or as an array element throws `JsonException`, because those positions cannot represent an absent property.

## Request IDs

`RequestId` supports implicit conversions from integers (`long`) and strings while preserving their JSON token types. It implements value equality and hashing, so it can be used as a dictionary key to correlate requests and responses.

```csharp
using System;
using ZYC.Codex.Protocol;

RequestId integerId = 1L;
RequestId stringId = "1";

Console.WriteLine(integerId == stringId); // False: JSON number 1 differs from string "1".
Console.WriteLine(integerId == new RequestIdInteger(1L)); // True
```

## Repository Structure

| Project | Target framework | Purpose |
| --- | --- | --- |
| [ZYC.Codex.Protocol](ZYC.Codex.Protocol/) | `netstandard2.0` | Protocol types, JSON converters, and `Optional<T>` |
| [ZYC.Codex.Protocol.Generator](ZYC.Codex.Protocol.Generator/) | `net10.0` | Generates C# types and converters from JSON Schema |
| [ZYC.Codex.Protocol.Generator.Tests](ZYC.Codex.Protocol.Generator.Tests/) | `net10.0` | Generator consistency and protocol serialization tests |

## Regenerating Protocol Code

Working on the generator and test projects requires the .NET 10 SDK. The default local workflow also requires Node.js/npm to export the latest Codex schema. `--generate-only` regenerates from the checked-in schema without Node.js or a Codex CLI installation.

Run from the repository root:

```shell
dotnet run --project ZYC.Codex.Protocol.Generator/ZYC.Codex.Protocol.Generator.csproj -c Release
```

Starting the Generator without arguments, including from the debugger, resolves the latest stable Codex version, exports its schema, generates code and metadata, updates README, and builds and packs locally. This always runs the generation flow, even if that package has already been published. It does not query NuGet, commit, push, or publish. Each stage logs its progress; build and pack output is forwarded to the console, and errors propagate to the debugger.

The Generator's `ProductInfo.cs` remains the manually maintained source of the library version and description. Its `CodexVersion` property contains the `__CODEX_VERSION__` placeholder and is never changed by the running Generator. When copying the file to the protocol project, a plain string replacement substitutes the runtime `codexVersion` for that placeholder. The `Version` expression is copied unchanged and evaluates using the substituted `CodexVersion`. The package version is obtained by the same placeholder replacement on the Generator's `ProductInfo.Version`; that version and the original description are written to `_version.props`, which remains an ignored, derived file. Version checks read the protocol project's generated `ProductInfo.cs` from disk.

`Directory.Build.props` supplies the default output directory `_bin/`. The Generator and test projects override `OutputPath` in their own `.csproj` files:

| Directory | Contents |
| --- | --- |
| `_bin/` | Protocol build output and NuGet packages (global default) |
| `_bin_build/` | Generator executable and dependencies |
| `_bin_test/` | Test output and copies of referenced assemblies |

The release flow builds the protocol before running tests. Tests reuse the already-built project references to avoid rebuilding and overwriting the running Generator. Build, test, and pack commands do not override `OutputPath`.

The Generator owns version checks, schema updates, Git operations, tests, packaging, and publication. Select a branch of the workflow with arguments after `--`:

| Arguments | Behavior |
| --- | --- |
| None | Export the latest stable Codex schema, generate code and metadata, update README, build, and pack locally |
| `--generate-only` | Regenerate from the checked-in schema and its recorded Codex version without building or packing; the original untracked snapshot requires an initial no-argument run or `--update` to establish its version |
| `--check-update` | Check npm/NuGet and report whether an update or publication is needed; do not change source files |
| `--update [CodexVersion]` | Export the specified stable Codex version (or npm latest), update version metadata and README, and regenerate code |
| `--publish` | Check, update if needed, build, pack, and publish; do not commit or push source |
| `--update-and-publish` | Check, update, build, test, commit, pack, push source, and publish as needed |

For example, to update the schema explicitly (replace the example version with the desired stable version):

```shell
dotnet run --project ZYC.Codex.Protocol.Generator/ZYC.Codex.Protocol.Generator.csproj -c Release -- --update 0.114.0
```

Schema export requires Windows, Node.js/npm on `PATH`, and access to the npm registry. It exports the exact `@openai/codex` version into an empty staging directory and generates against that schema before replacing the checked-in snapshot. Unsupported schema structures stop generation before replacing existing outputs or version metadata. Successful generation updates the protocol project's `ProductInfo.cs` and the version table above. The Generator's own `ProductInfo.cs` stays unchanged. A global Codex CLI installation is not required. `dotnet run` still builds the Generator itself in every mode.

The legacy `PUSH_NUGET` compilation symbol maps the no-argument invocation to `--publish`, preserving the original `push-nuget.yml` entry point. Explicit command-line arguments take precedence over that symbol.

To generate code from an existing schema without running the complete workflow, use the following in maintenance code that references the generator project:

```csharp
using ZYC.Codex.Protocol.Generator.Generation;
using ZYC.Codex.Protocol.Generator.Schema;

var schema = await SchemaLoader.LoadAsync(
    "ZYC.Codex.Protocol.Generator/json-schema/codex_app_server_protocol.schemas.json");
var types = new SchemaAnalyzer(schema).Analyze();

new CSharpGenerator().Write(types, "ZYC.Codex.Protocol");
```

These relative paths assume the repository root is the working directory. `Write` replaces the top-level `*.g.cs` files in the target directory. Make protocol mapping changes in the generator and regenerate the affected output.

Checked-in schema snapshots are stored in [ZYC.Codex.Protocol.Generator/json-schema](ZYC.Codex.Protocol.Generator/json-schema/). The generated types reflect the schema used during generation. After updating the Codex CLI, review protocol changes and synchronize the schema snapshots used for generation and testing. Unsupported schema structures produce errors that include their location and original content.

## Tests

Run from the repository root:

```shell
dotnet test ZYC.Codex.Protocol.Generator.Tests/ZYC.Codex.Protocol.Generator.Tests.csproj -c Release
```

The tests cover deterministic generation, snapshot consistency, naming conflicts, union and enum serialization, required and fixed field validation, the three states of `Optional<T>`, and `RequestId` value semantics.

## License

[MIT License](LICENSE).
