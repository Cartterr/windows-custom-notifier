# Development Instructions

This project is a centralized custom notifier for Windows 11. Although its initial module interacts with YouTube Data API v3, it is designed with the possibility to add more integrations later (such as OAuth with OpenAI or local integrations).

## Security and API Keys (CRITICAL)

**NEVER commit API keys or OAuth credentials directly into the repository.**

To ensure credentials remain local:
- The YouTube `credentials.json` and OAuth tokens are strictly saved inside `%LocalAppData%\YoutubeNotifier` (a folder completely isolated from this repository).
- If new integrations (like OpenAI) are added in the future, follow this exact same approach:
  - Create the authentication logic to read secrets from outside the repository.
  - Rely on Windows user folders (e.g., `%LocalAppData%\WindowsCustomNotifier`) for persistence.
  - If a secret absolutely must be within the folder, add it to `.gitignore` and create an example template (e.g., `.env.example`).
- Be extremely cautious when adding new `.json` config files. `.gitignore` ignores all JSONs by default unless whitelisted.

## Building and Running

You can use the .NET CLI:
```bash
dotnet build
dotnet run
```

## Adding New Integrations

When adding a new integration, consider maintaining the structure:
1. **Background Worker**: Polls endpoints periodically or listens to WebSockets.
2. **Notification Builder**: Generates rich Windows 11 Toasts (e.g., profile pictures, headers, custom texts, and buttons).
3. **Activation Handler**: Navigates the user to a browser or internal URL upon clicking.
