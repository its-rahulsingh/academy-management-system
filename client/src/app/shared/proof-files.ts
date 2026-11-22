/** A bill or receipt ready to upload: its name and a base64 data URL. */
export interface ProofFile {
  fileName: string;
  dataUrl: string;
  /** Bytes after shrinking, for showing the size. */
  size: number;
}

/** Longest side of an uploaded photo. Enough to read a bill; keeps a phone photo around 200–400 KB. */
const MAX_SIDE = 1600;

/**
 * Reads a chosen file for upload. Photos are shrunk and saved as JPEG (a 4 MB phone photo becomes a few hundred KB);
 * PDFs are sent as they are. Anything else, or anything still over the limit, is refused with a message.
 */
export async function readProofFile(file: File, maxMb: number): Promise<ProofFile> {
  const isPdf = file.type === 'application/pdf' || /\.pdf$/i.test(file.name);
  const isImage = /^image\/(jpeg|png|webp|heic|heif)$/.test(file.type) || /\.(jpe?g|png|webp)$/i.test(file.name);
  if (!isPdf && !isImage) throw new Error(`${file.name}: attach a photo (JPG, PNG, WebP) or a PDF.`);

  let dataUrl = await asDataUrl(file);
  let name = file.name;
  if (isImage) {
    const shrunk = await shrink(dataUrl).catch(() => null);
    // PNG screenshots of text can be smaller than a JPEG; keep whichever is smaller.
    if (shrunk && shrunk.length < dataUrl.length) {
      dataUrl = shrunk;
      name = name.replace(/\.(png|webp|heic|heif|jpeg)$/i, '.jpg');
    }
  }
  const size = Math.floor(((dataUrl.length - dataUrl.indexOf(',') - 1) * 3) / 4);
  if (size > maxMb * 1024 * 1024) throw new Error(`${file.name} is larger than ${maxMb} MB.`);
  return { fileName: name, dataUrl, size };
}

function asDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error('The file could not be read.'));
    reader.readAsDataURL(file);
  });
}

function shrink(dataUrl: string): Promise<string> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => {
      const scale = Math.min(1, MAX_SIDE / Math.max(image.width, image.height));
      const canvas = document.createElement('canvas');
      canvas.width = Math.round(image.width * scale);
      canvas.height = Math.round(image.height * scale);
      const context = canvas.getContext('2d');
      if (!context) return reject(new Error('no canvas'));
      context.fillStyle = '#fff';
      context.fillRect(0, 0, canvas.width, canvas.height);
      context.drawImage(image, 0, 0, canvas.width, canvas.height);
      resolve(canvas.toDataURL('image/jpeg', 0.82));
    };
    image.onerror = () => reject(new Error('not an image'));
    image.src = dataUrl;
  });
}

export function fileSize(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
