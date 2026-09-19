# ZYC.Codex.Protocol

.NET data contracts for the Codex app-server protocol, including requests, responses, notifications, union types, and `System.Text.Json` converters for constructing and parsing strongly typed protocol messages.

The library handles message modeling and JSON serialization. The consuming application manages the app-server process, transport, initialization handshake, request dispatch, and approval handling.

## Installation

Install [ZYC.Codex.Protocol from NuGet](https://www.nuget.org/packages/ZYC.Codex.Protocol) in your application project:

```shell
dotnet add package ZYC.Codex.Protocol
```

Import the `ZYC.Codex.Protocol` namespace to use the protocol types. JSON converters are applied through attributes on types and properties; no manual converter registration is required.

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

Working on the generator and test projects requires the .NET 10 SDK. The complete generation workflow also requires a `codex` command available on `PATH` that supports `app-server generate-json-schema`.

Run from the repository root:

```shell
dotnet run --project ZYC.Codex.Protocol.Generator/ZYC.Codex.Protocol.Generator.csproj -c Release
```

The entry point updates `_version.props`, invokes the Codex CLI to export the schema, generates protocol types and converters, and synchronizes `ProductInfo.cs`. It then builds and packs the protocol project, writing artifacts to `_bin/`. The package version comes from `ProductInfo.Version` in the generator project.

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
