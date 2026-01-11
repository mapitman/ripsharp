.PHONY: all build test format clean restore help

# Default target: build and test the solution
all: build test

# Show available targets
help:
	@echo "Available targets:"
	@echo "  make all       - Build the solution (default)"
	@echo "  make restore   - Restore NuGet packages"
	@echo "  make build     - Build the solution"
	@echo "  make format    - Run dotnet format"
	@echo "  make test      - Run tests"
	@echo "  make clean     - Clean build outputs"

# Restore NuGet packages
restore:
	dotnet restore media-encoding.sln

# Build the solution
build: restore
	dotnet build media-encoding.sln

# Run code formatter
format: restore
	dotnet format media-encoding.sln

# Run tests
test: build
	dotnet test media-encoding.sln

# Clean build outputs
clean:
	dotnet clean media-encoding.sln
