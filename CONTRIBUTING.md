# Contributing to SignalR Chat

Thank you for your interest in contributing! This document provides guidelines for contributing to the project.

## Code of Conduct

Be respectful, inclusive, and constructive in all interactions. We're here to learn and build together.

## How to Contribute

### 1. Setting Up Your Development Environment

```bash
# Fork and clone
git clone https://github.com/YOUR_USERNAME/SignalR-Chat.git
cd SignalR-Chat

# Build the solution
dotnet build ./src/Chat.sln

# Run tests
dotnet test src/Chat.sln

# Run locally (in-memory mode)
dotnet run --project ./src/Chat.Web --urls=http://localhost:5099
```

See [docs/development/local-setup.md](docs/development/local-setup.md) for detailed setup.

### 2. Making Changes

1. **Create a feature branch**:
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/bug-description
   ```

2. **Make your changes**:
   - Follow existing code style (C# conventions, 4-space indentation)
   - Add tests for new features
   - Update documentation if needed

3. **Run tests**:
   ```bash
   dotnet test src/Chat.sln
   ```

4. **Commit your changes**:
   ```bash
   git commit -m "feat: add new feature"
   # or
   git commit -m "fix: resolve issue with X"
   ```

### 3. Commit Message Conventions

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>: <description>

[optional body]

[optional footer]
```

**Types**:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `test`: Adding or updating tests
- `refactor`: Code refactoring
- `perf`: Performance improvements
- `chore`: Maintenance tasks

**Examples**:
```
feat: add message editing capability
fix: resolve race condition in read receipts
docs: update deployment guide for Azure
test: add integration tests for OTP flow
refactor: simplify message repository
```

### 4. Pull Request Process

1. **Push your branch**:
   ```bash
   git push origin feature/your-feature-name
   ```

2. **Create a Pull Request**:
   - Use a clear title and description
   - Reference any related issues (e.g., "Fixes #123")
   - Ensure all tests pass
   - Request review from maintainers

3. **PR Template** (automatically loaded):
   ```markdown
   ## Description
   [What does this PR do?]

   ## Related Issues
   Fixes #[issue number]

   ## Changes
   - [List key changes]

   ## Testing
   - [ ] All tests pass
   - [ ] Added new tests for new features
   - [ ] Tested locally

   ## Checklist
   - [ ] Code follows project style
   - [ ] Documentation updated
   - [ ] No breaking changes (or documented)
   ```

4. **Code Review**:
   - Address reviewer feedback
   - Keep PR scope focused (one feature/fix per PR)
   - Be responsive to comments

## Development Guidelines

### Code Style

- **C#**: Follow [Microsoft C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- **JavaScript**: Use modern ES6+, avoid jQuery
- **Indentation**: 4 spaces (C#), 2 spaces (JS, JSON)
- **Naming**: PascalCase (classes, methods), camelCase (local variables, parameters)

### Testing

- **Unit tests**: For business logic, utilities, services
- **Integration tests**: For API endpoints, SignalR hubs, auth flows
- **Coverage**: Aim for >80% coverage on new code

```bash
# Run all tests
dotnet test src/Chat.sln

# Run specific test project
dotnet test tests/Chat.Tests/
dotnet test tests/Chat.IntegrationTests/

# Run with coverage (if configured)
dotnet test src/Chat.sln /p:CollectCoverage=true
```

### Security

If you discover a security vulnerability:
1. **Do NOT** open a public issue
2. Email security concerns to: [your-email@example.com]
3. Include: description, steps to reproduce, impact

## Project Structure

```
src/Chat.Web/
├── Controllers/        # REST API endpoints
├── Hubs/              # SignalR hubs
├── Pages/             # Razor Pages
├── Services/          # Business logic
├── Repositories/      # Data access
├── Middleware/        # Request pipeline
├── Models/            # Domain models
├── Options/           # Configuration classes
└── Utilities/         # Helpers, extensions

tests/
├── Chat.Tests/              # Unit tests
├── Chat.IntegrationTests/   # Integration tests
└── Chat.Web.Tests/          # Web/security tests
```

## Documentation

When adding features:
1. Update relevant `/docs` files
2. Add code comments for complex logic
3. Update README.md if user-facing
4. Consider adding ADR (Architecture Decision Record) in `docs/architecture/decisions/`

## Getting Help

- **Questions**: Open a [GitHub Discussion](https://github.com/smereczynski/SignalR-Chat/discussions)
- **Bugs**: Open a [GitHub Issue](https://github.com/smereczynski/SignalR-Chat/issues)
- **Chat**: [Add Discord/Slack link if available]

## What to Contribute

Looking for ideas? Check:
- [Issues labeled "good first issue"](https://github.com/smereczynski/SignalR-Chat/labels/good%20first%20issue)
- [Issues labeled "help wanted"](https://github.com/smereczynski/SignalR-Chat/labels/help%20wanted)
- [Project roadmap](https://github.com/smereczynski/SignalR-Chat/projects)

### Good First Contributions

- Improve documentation
- Add more localization languages
- Write additional tests
- Fix typos or formatting
- Improve error messages

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

---

Thank you for contributing! 🎉
