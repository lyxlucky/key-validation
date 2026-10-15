export function downloadText(value: string, fileName: string, contentType = 'text/plain;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([value], { type: contentType }));
  const anchor = document.createElement('a');
  anchor.href = url; anchor.download = fileName;
  document.body.appendChild(anchor); anchor.click(); anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
