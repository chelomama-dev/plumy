/**
 * Hands the browser a file it downloaded through HttpClient (which adds the bearer token a plain link can't), so
 * it saves it with this name.
 */
export function saveFile(document: Document, blob: Blob, fileName: string): void {
  // A temporary link to the downloaded bytes, clicked by code so the file keeps its name
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  // Freed on the next tick: revoking straight away can cancel the download in some browsers
  setTimeout(() => URL.revokeObjectURL(url));
}

/**
 * The file name a Content-Disposition header gives (RFC 6266): the UTF-8 `filename*` if there is one, else
 * `filename`. Null when there is neither.
 */
export function fileNameFromContentDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1].trim());
    } catch {
      // Malformed: fall back to the plain name
    }
  }
  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/i.exec(header);
  return plain ? (plain[2] ?? plain[1]).trim() : null;
}
