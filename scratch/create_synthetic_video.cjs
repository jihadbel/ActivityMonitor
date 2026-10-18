// Test-only synthetic canvas. No desktop, camera, microphone, profile or network access.
const { chromium } = require('C:/Users/Masoud/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs = require('node:fs');
(async () => {
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  try {
    const page = await browser.newPage();
    const bytes = await page.evaluate(async () => {
      const canvas = document.createElement('canvas'); canvas.width = 64; canvas.height = 64;
      document.body.append(canvas);
      const ctx = canvas.getContext('2d');
      const mime = ['video/mp4;codecs=vp9', 'video/mp4;codecs=avc1.42001E', 'video/mp4'].find(x => MediaRecorder.isTypeSupported(x));
      if (!mime) throw new Error('Browser MP4 encoding is unavailable.');
      const stream = canvas.captureStream(10);
      const recorder = new MediaRecorder(stream, { mimeType: mime, videoBitsPerSecond: 100000 });
      const chunks = [];
      const completed = new Promise((resolve, reject) => { recorder.ondataavailable = e => chunks.push(e.data); recorder.onstop = resolve; recorder.onerror = reject; });
      let frame = 0;
      const draw = () => { ctx.fillStyle = '#224488'; ctx.fillRect(0, 0, 64, 64); ctx.fillStyle = '#ffffff'; ctx.fillRect((frame++ * 3) % 48, 24, 16, 16); };
      draw(); recorder.start(200);
      const timer = setInterval(draw, 100);
      await new Promise(resolve => setTimeout(resolve, 1200));
      recorder.stop(); await completed; clearInterval(timer); stream.getTracks().forEach(x => x.stop());
      return Array.from(new Uint8Array(await new Blob(chunks, { type: mime }).arrayBuffer()));
    });
    if (bytes.length === 0) throw new Error('Encoder produced an empty fixture.');
    fs.mkdirSync('ActivityMonitor.Tests/Fixtures', { recursive: true });
    fs.writeFileSync('ActivityMonitor.Tests/Fixtures/synthetic.mp4', Buffer.from(bytes));
    process.stdout.write(`Generated ${bytes.length} bytes of synthetic MP4.\n`);
  } finally { await browser.close(); }
})().catch(error => { process.stderr.write(error.stack + '\n'); process.exitCode = 1; });
