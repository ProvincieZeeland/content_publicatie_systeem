# CPS Management Tool

This Windows Forms application provides a simple interface to generate RS256 JWT tokens using a PFX certificate. It is designed to help developers and administrators quickly create signed JWTs for authentication or testing purposes.

## Features

- Select a `.pfx` certificate file using a file dialog
- Prompt for certificate password and clientId
- Generate RS256 JWT tokens signed with the selected certificate
- Display the generated JWT in a popup for easy copy/paste

## Getting Started

### Prerequisites

- Windows OS
- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) or later
- Visual Studio 2022+ (recommended) or use `dotnet` CLI

### Build Instructions

1. Clone this repository or download the source code.
2. Open the solution in Visual Studio and build, or run:
  ```pwsh
  dotnet build
  ```

### Run Instructions

You can run the application from Visual Studio or with the following command:

```pwsh
dotnet run --project CPS-ManagementTool.csproj
```

Alternatively, run the built executable from the `bin/Debug` or `bin/Release` folder.

## Usage

1. Launch the application.
2. When prompted, select your `.pfx` certificate file.
3. Enter the certificate password and clientId when prompted.
4. The generated JWT token will be displayed in a popup window.

## Dependencies

The following NuGet packages are required:

- System.IdentityModel.Tokens.Jwt
- Microsoft.IdentityModel.Tokens
- Microsoft.IdentityModel.Abstractions