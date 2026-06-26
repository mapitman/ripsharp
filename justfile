# Default target: build and test the solution
all: build test

# Show available targets
help:
	@echo "Available targets:"
	@echo "  just all       - Build and test the solution (default)"
	@echo "  just restore   - Restore NuGet packages"
	@echo "  just build     - Build the solution"
	@echo "  just format    - Run dotnet format"
	@echo "  just test      - Run tests"
	@echo "  just pack      - Build NuGet package (Release)"
	@echo "  just install   - Install ripsharp as a global dotnet tool (local package)"
	@echo "  just clean     - Clean build outputs"

# Restore NuGet packages
restore:
	dotnet restore RipSharp.sln

# Build the solution
build: restore
	dotnet build RipSharp.sln

# Run code formatter
format:
	dotnet format RipSharp.sln

# Run tests
test: build
	dotnet test RipSharp.sln

# Build NuGet package (Release)
pack: build
	dotnet pack -c Release src/RipSharp

# Install ripsharp as a global dotnet tool from local package output
install: pack
	@dotnet tool uninstall --global BugZapperLabs.RipSharp >/dev/null 2>&1 || true
	@dotnet tool install --global BugZapperLabs.RipSharp --add-source src/RipSharp/bin/Release

# Clean build outputs
clean:
	dotnet clean RipSharp.sln
