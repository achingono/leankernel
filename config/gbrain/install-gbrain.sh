#!/usr/bin/env sh
set -eu

# GBrain is installed via Bun from github:garrytan/gbrain.
# This helper is for local non-Docker development and ensures `gbrain`
# is available on PATH from Bun's global bin directory.
curl -fsSL https://bun.sh/install | bash
export PATH="$HOME/.bun/bin:$PATH"
bun install -g github:garrytan/gbrain
gbrain --version
