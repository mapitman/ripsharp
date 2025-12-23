#!/bin/bash
#
# Wrapper script to rip and encode a TV series disc
#
# Usage: ./rip_tv.sh --title "Show Name" --season 1 --output /path/to/output
#

set -e

# Default values
DISC="disc:0"
OUTPUT_DIR=""
TEMP_DIR=""
TITLE=""
YEAR=""
SEASON="1"
DISC_TYPE=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --disc)
            DISC="$2"
            shift 2
            ;;
        --output)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --temp)
            TEMP_DIR="$2"
            shift 2
            ;;
        --title)
            TITLE="$2"
            shift 2
            ;;
        --season)
            SEASON="$2"
            shift 2
            ;;
        --year)
            YEAR="$2"
            shift 2
            ;;
        --disc-type)
            DISC_TYPE="$2"
            shift 2
            ;;
        --help)
            echo "Usage: $0 --output OUTPUT_DIR [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --disc DISC        Disc path (default: disc:0)"
            echo "  --output DIR       Output directory (required)"
            echo "  --temp DIR         Temporary directory (default: OUTPUT_DIR/.makemkv)"
            echo "  --title TITLE      TV series title"
            echo "  --season NUM       Season number (default: 1)"
            echo "  --disc-type TYPE   Override disc type (dvd|bd|uhd)"
            echo "  --help             Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Check required arguments
if [ -z "$OUTPUT_DIR" ]; then
    echo "Error: --output is required"
    echo "Use --help for usage information"
    exit 1
fi

# Default temp directory inside output if not provided
if [ -z "$TEMP_DIR" ]; then
    TEMP_DIR="$OUTPUT_DIR/.makemkv"
fi

# Build command array
CMD_ARGS=(python3 rip_disc.py --output "$OUTPUT_DIR" --tv)

if [ -n "$DISC" ]; then
    CMD_ARGS+=(--disc "$DISC")
fi

if [ -n "$TEMP_DIR" ]; then
    CMD_ARGS+=(--temp "$TEMP_DIR")
fi

if [ -n "$TITLE" ]; then
    CMD_ARGS+=(--title "$TITLE")
fi

if [ -n "$SEASON" ]; then
    CMD_ARGS+=(--season "$SEASON")
fi

if [ -n "$YEAR" ]; then
    CMD_ARGS+=(--year "$YEAR")
fi

if [ -n "$DISC_TYPE" ]; then
    CMD_ARGS+=(--disc-type "$DISC_TYPE")
fi

# Execute
echo "Ripping TV series disc..."
echo "Using temp directory: $TEMP_DIR"
"${CMD_ARGS[@]}"
