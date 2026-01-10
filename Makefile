.PHONY: help venv install clean activate rip-tv

# Use bash for all recipe commands
SHELL := /bin/bash

# Default Python version
PYTHON := python3
VENV := venv
BIN := $(VENV)/bin
CURRENT_SHELL := $(shell basename $$SHELL)

# Defaults for rip targets (override via: make rip-movie OUTPUT=... EXTRA_ARGS="--title Foo")
OUTPUT ?= $(HOME)/Videos
EXTRA_ARGS ?=

help:
	@echo "Available targets:"
	@echo ""
	@echo "Setup:"
	@echo "  make all       - Create venv and install dependencies (same as 'make install')"
	@echo "  make install   - Create venv and install dependencies (same as 'make all')"
	@echo "  make venv      - Create virtual environment only"
	@echo ""
	@echo "Activation:"
	@echo "  make activate  - Start a shell with virtual environment activated"
	@echo ""
	@echo "Usage:"
	@echo "  make rip-movie - Rip a movie disc (use OUTPUT=/path and EXTRA_ARGS='--title \"Title\" --year 2024')"
	@echo "  make rip-tv    - Rip a TV series disc (use OUTPUT=/path and EXTRA_ARGS='--title \"Show\" --season 1')"
	@echo ""
	@echo "Cleanup:"
	@echo "  make clean     - Remove virtual environment"

venv:
	@echo "Creating virtual environment..."
	$(PYTHON) -m venv $(VENV)
	@echo "Virtual environment created in $(VENV)/"
	@echo "Activate with: source $(BIN)/activate"

install: venv
	@echo "Installing dependencies..."
	$(BIN)/pip install --upgrade pip
	$(BIN)/pip install -r requirements.txt
	@echo "Dependencies installed successfully!"

activate: venv
	@echo "Starting $(CURRENT_SHELL) with virtual environment activated..."
	@$(BIN)/python -c "import sys; print(f'Python {sys.version}')"
	@echo "Virtual environment is active. Type 'exit' to return."
	@if [ "$(CURRENT_SHELL)" = "zsh" ]; then \
		zsh -i -c ". $(BIN)/activate; exec zsh -i"; \
	elif [ "$(CURRENT_SHELL)" = "bash" ]; then \
		bash -i -c ". $(BIN)/activate; exec bash -i"; \
	else \
		sh -i -c ". $(BIN)/activate; exec sh -i"; \
	fi


all: install

rip-movie: venv
	@echo "Ripping movie to $(OUTPUT)..."
	@. $(BIN)/activate; ./rip_movie.sh --output "$(OUTPUT)" $(EXTRA_ARGS)

rip-tv: venv
	@echo "Ripping TV to $(OUTPUT)..."
	@. $(BIN)/activate; ./rip_tv.sh --output "$(OUTPUT)" $(EXTRA_ARGS)

clean:
	@echo "Removing virtual environment..."
	rm -rf $(VENV)
	@echo "Virtual environment removed."
