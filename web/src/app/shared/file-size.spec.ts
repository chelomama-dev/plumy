import { formatFileSize } from './file-size';

describe('formatFileSize', () => {
  it('uses bytes, KB or MB with a Spanish decimal comma', () => {
    expect(formatFileSize(830)).toBe('830 B');
    expect(formatFileSize(1024)).toBe('1 KB');
    expect(formatFileSize(250_880)).toBe('245 KB');
    expect(formatFileSize(1.25 * 1024 * 1024)).toBe('1,3 MB');
    expect(formatFileSize(10 * 1024 * 1024)).toBe('10 MB');
  });
});
