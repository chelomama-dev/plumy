const number = new Intl.NumberFormat('es-ES', { maximumFractionDigits: 1 });

/** "830 B", "245 KB", "1,2 MB". Binary units (1 KB = 1024 B), like the 10 MB limit. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${number.format(bytes / 1024)} KB`;
  }
  return `${number.format(bytes / (1024 * 1024))} MB`;
}
