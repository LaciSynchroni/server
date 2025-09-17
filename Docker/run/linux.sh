#!/usr/bin/env bash
STANDALONE=false
SHARDED=false
START=false
STOP=false

for arg in "$@"; do
  case $arg in
    --standalone) STANDALONE=true ;;
    --sharded)    SHARDED=true ;;
    start)      START=true ;;
    stop)       STOP=true ;;
    *) echo "Unknown option: $arg" && exit 1 ;;
  esac
done

if $STANDALONE && $SHARDED; then
  echo "❌ You cannot use -standalone and -sharded together."
  exit 1
fi

if ! $STANDALONE && ! $SHARDED; then
  echo "❌ You must specify either -standalone or -sharded."
  exit 1
fi

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

import_dotenv() {
    local env_file="${1:-.env}"
    
    # Check if file exists
    if [[ ! -f "$env_file" ]]; then
        echo "Warning: Env file not found: $env_file" >&2
        return 1
    fi
    
    # Process each line in the file
    while IFS= read -r line || [[ -n "$line" ]]; do
        # Skip empty lines and comments
        if [[ -z "$line" || "$line" =~ ^[[:space:]]*$ || "$line" =~ ^[[:space:]]*# ]]; then
            continue
        fi
        
        # Check if line contains '='
        if [[ "$line" == *"="* ]]; then
            # Split on first '=' occurrence
            name="${line%%=*}"
            value="${line#*=}"
            
            # Trim whitespace from name and value
            name="${name#"${name%%[![:space:]]*}"}"   # trim leading
            name="${name%"${name##*[![:space:]]}"}"   # trim trailing
            value="${value#"${value%%[![:space:]]*}"}" # trim leading
            value="${value%"${value##*[![:space:]]}"}" # trim trailing
            
            # Remove wrapping quotes if present
            if [[ "$value" =~ ^\".*\"$ ]] || [[ "$value" =~ ^\'.*\'$ ]]; then
                value="${value:1:-1}"
            fi
            
            # Export the variable and show confirmation
            export "$name=$value"
            echo "+ $name"
        fi
    done < "$env_file"
}

import_dotenv "./compose/.env.local"

if $STANDALONE; then
  if [ ! -f config/standalone/base.appsettings.json ]; then
    echo "Base appsettings not found!"
    exit 1
  elif [ ! -f config/standalone/authservice.appsettings.json ]; then
    echo "Auth service appsettings not found!"
    exit 1
  elif [ ! -f config/standalone/files.appsettings.json ]; then
    echo "Files appsettings not found!"
    exit 1
  elif [ ! -f config/standalone/server.appsettings.json ]; then
    echo "Server appsettings not found!"
    exit 1
  elif [ ! -f config/standalone/services.appsettings.json ]; then
    echo "Services appsettings not found!"
    exit 1
  fi
fi

if $START; then
  if $STANDALONE; then
    echo "🚀 Starting in Standalone mode..."
    docker compose -f "$SCRIPT_DIR/compose/standalone.yml" -p standalone up -d
  elif $SHARDED; then
    echo "🚀 Starting in Sharded mode..."
    docker compose -f "$SCRIPT_DIR/compose/sharded.yml" -p sharded up -d
  fi
elif $STOP; then
  if $STANDALONE; then
    echo "🛑 Stopping Standalone service..."
    docker compose -f "$SCRIPT_DIR/compose/standalone.yml" -p standalone stop
  elif $SHARDED; then
    echo "🛑 Stopping Sharded service..."
    docker compose -f "$SCRIPT_DIR/compose/sharded.yml" -p sharded stop
  fi
else
  # neither -start nor -stop supplied
  if $STANDALONE; then
    echo "⚡ Running other Standalone action..."
    docker compose -f "$SCRIPT_DIR/compose/standalone.yml" -p standalone up
  elif $SHARDED; then
    echo "⚡ Running other Sharded action..."
    docker compose -f "$SCRIPT_DIR/compose/sharded.yml" -p sharded up
  fi
fi
