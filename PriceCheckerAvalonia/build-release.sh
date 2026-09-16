#!/usr/bin/env bash
set -euo pipefail

VERSION="$1"
OUTPUT_DIR="artifacts"
NOTES="${2-}"
RUNTIMES=("win-x64" "linux-x64")

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$SCRIPT_DIR/PriceCheckerAvalonia/PriceCheckerAvalonia.csproj"
if [ ! -f "$PROJ" ]; then
  echo "Project file not found: $PROJ" >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"

publish_and_pack() {
  local rid="$1"
  local publish_dir
  publish_dir="$(mktemp -d)/pricechecker-publish-$VERSION-$rid"

  echo "Publishing for $rid..."
  dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true /p:PublishSingleFile=true -o "$publish_dir"

  if [[ "$rid" == win-* ]]; then
	artifact="$OUTPUT_DIR/pricechecker-$VERSION-$rid.zip"
	echo "Creating ZIP: $artifact"
	rm -f "$artifact"
	(cd "$publish_dir" && zip -r "$SCRIPT_DIR/$artifact" .) || (cd "$publish_dir" && python -m zipfile -c "$SCRIPT_DIR/$artifact" .)
  else
	artifact="$OUTPUT_DIR/pricechecker-$VERSION-$rid.tar.gz"
	echo "Creating tar.gz: $artifact"
	rm -f "$artifact"
	tar -C "$publish_dir" -czf "$artifact" .
  fi

  sha256=$(sha256sum "$artifact" | awk '{print $1}')
  publishedAt=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  fileName=$(basename "$artifact")

  cat > "$OUTPUT_DIR/metadata-$VERSION-$rid.json" <<EOF
{
  "version": "$VERSION",
  "url": "https://updates.example.com/$fileName",
  "sha256": "$sha256",
  "publishedAt": "$publishedAt",
  "notes": "$NOTES",
  "minClientVersion": "0.0.0"
}
EOF

  if command -v gpg >/dev/null 2>&1; then
	gpg --batch --yes --detach-sign --armor --output "$artifact.sig" --sign "$artifact" || echo "gpg sign failed"
  fi

  echo "Artifact: $artifact"
  echo "Metadata: $OUTPUT_DIR/metadata-$VERSION-$rid.json"
}

for rid in "${RUNTIMES[@]}"; do
  publish_and_pack "$rid"
done

echo "Done. Artifacts in $OUTPUT_DIR"
