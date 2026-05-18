#!/bin/bash
echo "============================================"
echo " PharmaDMI - Digital Manufacturing Intelligence"
echo "============================================"
echo ""

# AI: An open-source LLM (Llama family) is used by default - no setup required.
# Optional overrides (any one of these will be auto-detected):
#   export OLLAMA_URL="http://localhost:11434"  &&  export OLLAMA_MODEL="llama3.2"
#   export HF_TOKEN="hf_xxx"                    &&  export HF_MODEL="meta-llama/Meta-Llama-3-8B-Instruct"
#   export ANTHROPIC_API_KEY="sk-ant-..."       # Claude, optional

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "[1/3] Starting Telemetry Service on port 5001..."
cd "$SCRIPT_DIR/services/TelemetryService"
dotnet run &
TELEMETRY_PID=$!
sleep 3

echo "[2/3] Starting Alert Service on port 5002..."
cd "$SCRIPT_DIR/services/AlertService"
dotnet run &
ALERT_PID=$!
sleep 3

echo "[3/3] Starting Insight Service on port 5003..."
cd "$SCRIPT_DIR/services/InsightService"
dotnet run &
INSIGHT_PID=$!
sleep 2

echo ""
echo "============================================"
echo " All services running!"
echo ""
echo " Telemetry : http://localhost:5001/swagger"
echo " Alerts    : http://localhost:5002/swagger"
echo " Insights  : http://localhost:5003/swagger"
echo " Dashboard : Open angular-ui/index.html in browser"
echo ""
echo " PIDs: Telemetry=$TELEMETRY_PID Alert=$ALERT_PID Insight=$INSIGHT_PID"
echo " To stop all: kill $TELEMETRY_PID $ALERT_PID $INSIGHT_PID"
echo "============================================"

# Open browser
sleep 2
if command -v xdg-open &>/dev/null; then
  xdg-open "$SCRIPT_DIR/angular-ui/index.html"
elif command -v open &>/dev/null; then
  open "$SCRIPT_DIR/angular-ui/index.html"
fi

# Wait for all background processes
wait $TELEMETRY_PID $ALERT_PID $INSIGHT_PID
