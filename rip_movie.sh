#!/bin/bash
#
# Wrapper script to rip and encode a movie disc
#
# Usage: ./rip_movie.sh --title "Movie Name" --year 2024 --output /path/to/output
#

set -e

# Default values
DISC="disc:0"
OUTPUT_DIR=""
TEMP_DIR="/tmp/makemkv"
TITLE=""
YEAR=""

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
        --year)
            YEAR="$2"
            shift 2
            ;;
        --help)
            echo "Usage: $0 --output OUTPUT_DIR [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --disc DISC        Disc path (default: disc:0)"
            echo "  --output DIR       Output directory (required)"
            echo "  --temp DIR         Temporary directory (default: /tmp/makemkv)"
            echo "  --title TITLE      Movie title"
            echo "  --year YEAR        Release year"
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

# Build command
CMD="python3 rip_disc.py --output \"$OUTPUT_DIR\""

if [ -n "$DISC" ]; then
    CMD="$CMD --disc \"$DISC\""
fi

if [ -n "$TEMP_DIR" ]; then
    CMD="$CMD --temp \"$TEMP_DIR\""
fi

if [ -n "$TITLE" ]; then
    CMD="$CMD --title \"$TITLE\""
fi

if [ -n "$YEAR" ]; then
    CMD="$CMD --year $YEAR"
fi

# Execute
echo "Ripping movie disc..."
eval "$CMD"
