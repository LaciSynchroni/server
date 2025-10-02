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
  echo "❌ You cannot use --standalone and --sharded together."
  exit 1
fi

if ! $STANDALONE && ! $SHARDED; then
  echo "❌ You must specify either --standalone or --sharded."
  exit 1
fi

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

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
