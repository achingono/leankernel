# signal-cli Setup for LeanKernel

## Prerequisites
- Java 21+ runtime
- signal-cli binary (https://github.com/AsamK/signal-cli)

## Registration
```bash
# Register a new number (requires SMS verification)
signal-cli -u +1234567890 register

# Verify with the code received via SMS
signal-cli -u +1234567890 verify 123-456

# Test sending a message
signal-cli -u +1234567890 send -m "Hello from LeanKernel" +0987654321
```

## JSON-RPC Mode (used by LeanKernel)
```bash
# Start signal-cli in JSON-RPC mode (stdin/stdout)
signal-cli -u +1234567890 jsonRpc
```

LeanKernel's `SignalChannel` adapter manages this process automatically.
Set your phone number in `.env` as `SIGNAL_PHONE_NUMBER`.
