#!/usr/bin/env bash
# Creates the release signing identity on this Mac (once): a self-signed code-signing certificate
# in its own keychain, unlocked by build.sh with a password kept in the login keychain.
#
# Why: macOS remembers privacy grants (Full Disk Access, Desktop/Documents/Downloads, Automation)
# by the app's designated requirement. With an ad-hoc signature that is the build's hash, so every
# update loses them. Signed with one certificate it is `identifier … and certificate leaf = …`,
# the same for every release. Gatekeeper still treats the app as unnotarized either way.
#
# Back up ~/Library/Keychains/folder-splitter-signing.keychain-db together with its password
# (login keychain item "folder-splitter-signing-keychain"): a new certificate means users grant
# permissions once more.
set -euo pipefail

name="Folder Splitter Signing (HSoo-Kim)"
service="folder-splitter-signing-keychain"
keychain="$HOME/Library/Keychains/folder-splitter-signing.keychain-db"

if [[ -f "$keychain" ]]; then
    echo "already exists: $keychain" >&2
    exit 1
fi

umask 077
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

password="$(openssl rand -hex 24)"
security add-generic-password -a "$USER" -s "$service" -l "Folder Splitter signing keychain" -w "$password" -U
security create-keychain -p "$password" "$keychain"
security set-keychain-settings "$keychain"   # no auto-lock

openssl req -x509 -newkey rsa:2048 -sha256 -days 3650 -nodes -subj "/CN=$name" \
    -addext "basicConstraints=critical,CA:false" -addext "keyUsage=critical,digitalSignature" \
    -addext "extendedKeyUsage=critical,codeSigning" -keyout "$work/key.pem" -out "$work/cert.pem" 2>/dev/null
openssl pkcs12 -export -inkey "$work/key.pem" -in "$work/cert.pem" -name "$name" -passout pass:import -out "$work/identity.p12"
security import "$work/identity.p12" -k "$keychain" -P import -T /usr/bin/codesign >/dev/null
# Lets codesign use the key without a keychain prompt.
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$keychain" >/dev/null

# codesign looks identities up through the user's keychain search list.
existing=()
while IFS= read -r line; do
    line="${line#"${line%%[![:space:]]*}"}"; line="${line#\"}"; line="${line%\"}"
    [[ -n "$line" ]] && existing+=( "$line" )
done < <(security list-keychains -d user)
security list-keychains -d user -s "${existing[@]}" "$keychain"

security find-identity -p codesigning "$keychain"
