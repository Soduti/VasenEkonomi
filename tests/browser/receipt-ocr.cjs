const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
process.chdir(root);
fs.mkdirSync('artifacts/ocr-check', { recursive: true });
const razor = fs.readFileSync('src/Vasen.Web/Pages/Receipts/Edit.cshtml','utf8');
const dialog = razor.match(/    <dialog[\s\S]*?<\/dialog>/)[0];

const ocrPanel = razor.match(/        <section id="receipt-ocr"[\s\S]*?<\/section>/)[0].replace('@Url.Content("~/lib/tesseract/")','/vasen/lib/tesseract/').replace('@Url.Content("~/js/receipt-ocr-worker.js")','/vasen/js/receipt-ocr-worker.js');
const html = '<!doctype html><html lang="sv"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/vasen/css/site.css"><body><main class="page-shell"><div class="receipts-page receipt-edit-page narrow"><form class="panel receipt-edit-form"><label>Belopp<input id="Amount" value="50,00"></label><label>Datum<input id="Date" type="date" value="2025-01-01"></label><input type="file" id="Uploads" name="Uploads" multiple><div id="file-previews"></div>' + ocrPanel + dialog + '<button id="save">Spara utlägg</button></form></div></main><script src="/vasen/js/receipt-ocr-parser.js" defer></script><script src="/vasen/js/receipt-ocr.js" defer></script><script src="/vasen/js/receipt-crop.js" defer></script></body></html>';
const requests = [];
const server = http.createServer((req,res) => {
    requests.push(req.url); if(req.url === '/favicon.ico') { res.writeHead(204);res.end();return; }
    res.setHeader('Content-Security-Policy',"default-src 'self'; img-src 'self' blob:; style-src 'self'; script-src 'self' 'wasm-unsafe-eval'; worker-src 'self'; object-src 'none'");
    if(req.url === '/vasen/') { res.setHeader('Content-Type','text/html; charset=utf-8'); res.end(html); return; }
    const relative = decodeURIComponent(req.url.split('?')[0]).replace(/^\/vasen\//,'');
    const publicRoot = path.join(root,'src/Vasen.Web/wwwroot');
    const file = path.resolve(publicRoot,relative);
    if (!file.startsWith(publicRoot + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404);res.end();return; }
    res.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : 'application/octet-stream');
    res.end(fs.readFileSync(file));
});
(async() => {
    await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
    const port = server.address().port;
    const cache = path.join(process.env.LOCALAPPDATA || '', 'ms-playwright');
    const installed = fs.existsSync(cache) ? fs.readdirSync(cache).filter(name => /^chromium-\d+$/.test(name)).sort().reverse() : [];
    const chrome = process.env.CROP_TEST_CHROME || installed.map(name => path.join(cache, name, 'chrome-win64/chrome.exe')).find(file => fs.existsSync(file));
    if (!chrome) throw Error('Set CROP_TEST_CHROME to an installed Chromium/Chrome executable.');
    const browser = spawn(chrome,['--headless=new','--no-sandbox','--disable-gpu','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+path.join(root,'artifacts/ocr-check/profile3'),'about:blank'],{windowsHide:true,stdio:['ignore','ignore','pipe']});
    let stderr = '';
    const endpoint = await new Promise((resolve,reject) => {
        browser.stderr.on('data', chunk => { stderr += chunk; const m=stderr.match(/DevTools listening on (ws:\/\/[^\s]+)/); if(m) resolve(m[1]); });
        browser.on('error',reject); browser.on('exit', code=>reject(Error('Chrome exited '+code)));
    });
    const socket = new WebSocket(endpoint);
    await new Promise(resolve=>socket.addEventListener('open',resolve,{once:true}));
    let serial=0, pending=new Map(), session, issues=[];
    socket.addEventListener('message',({data})=>{
        const msg=JSON.parse(data);
        if(msg.id) { const pair=pending.get(msg.id); pending.delete(msg.id); if(msg.error) pair.reject(Error(JSON.stringify(msg.error))); else pair.resolve(msg.result); }
        if(msg.method==='Runtime.exceptionThrown') issues.push(msg.params.exceptionDetails.text);
        if(msg.method==='Log.entryAdded' && msg.params.entry.level==='error') issues.push(msg.params.entry.text);
    });
    const send=(method,params={},sid=session)=>new Promise((resolve,reject)=>{
        const id=++serial; const timer=setTimeout(()=>{pending.delete(id);reject(Error('Protocol timeout '+method))},60000); pending.set(id,{resolve:v=>{clearTimeout(timer);resolve(v)},reject:e=>{clearTimeout(timer);reject(e)}});
        socket.send(JSON.stringify({id,method,params,...(sid?{sessionId:sid}:{})}));
    });
    const evaluate=async(expression)=> {
        const result=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
        if(result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
        return result.result.value;
    };
    const wait=async(expression)=> { for(let i=0;i<3000;i++){if(await evaluate(expression))return;await new Promise(r=>setTimeout(r,30));}throw Error('Timed out: '+expression); };
    try {
        const target=await send('Target.createTarget',{url:'about:blank'},null);
        session=(await send('Target.attachToTarget',{targetId:target.targetId,flatten:true},null)).sessionId;
        await send('Runtime.enable'); await send('Log.enable'); await send('Page.enable');
        await send('Emulation.setDeviceMetricsOverride',{width:320,height:740,deviceScaleFactor:1,mobile:true});
        await send('Page.navigate',{url:'http://127.0.0.1:'+port+'/vasen/'});
        await wait('!!window.VasenReceiptParser && !!document.querySelector("#Uploads")');
        assert.ok(!requests.some(url=>url.includes('/lib/tesseract/')),'OCR dependencies load on demand');
        await evaluate(`window.chooseReceipt = async function() {
            const canvas=document.createElement('canvas');canvas.width=800;canvas.height=1000;
            const c=canvas.getContext('2d');c.fillStyle='#fff';c.fillRect(0,0,800,1000);
            c.fillStyle='#000';c.font='36px monospace';
            ['VASEN TESTBUTIK','KVITTO','Datum 2026-11-10','Virke 1234,50','Moms 246,90','TOTALT 1234,50'].forEach((line,i)=>c.fillText(line,50,90+i*90));
            const blob=await new Promise(r=>canvas.toBlob(r,'image/png'));
            const transfer=new DataTransfer();transfer.items.add(new File([blob],'receipt.png',{type:'image/png'}));
            transfer.items.add(new File(['%PDF-test'],'receipt.pdf',{type:'application/pdf'}));
            document.querySelector('#Uploads').files=transfer.files;
            document.querySelector('#Uploads').dispatchEvent(new Event('change'));
        }; chooseReceipt();`);
        await wait('document.querySelector("#receipt-crop").open && !document.querySelector("#crop-apply").disabled');
        await evaluate('document.querySelector("#crop-apply").click()');
        await wait('!document.querySelector("#receipt-crop").open');
        assert.equal(await evaluate('document.querySelectorAll("[data-receipt-read]").length'),1,'PDF has no OCR button');
        await evaluate('document.querySelector("[data-receipt-read]").click()');
        await wait('!document.querySelector("#receipt-ocr").hasAttribute("aria-busy")');
        const found=await evaluate('({status:document.querySelector("[data-ocr-status]").textContent,date:document.querySelector("[data-ocr-date]").value,amount:document.querySelector("[data-ocr-amount]").value})');
        assert.equal(found.date,'2026-11-10',JSON.stringify(found));
        assert.equal(found.amount,'1234,50',JSON.stringify(found));
        assert.deepEqual(await evaluate('[document.querySelector("#Date").value,document.querySelector("#Amount").value]'),['2025-01-01','50,00'],'never overwrite fields automatically');
        console.log('PASS: real Swedish/English OCR with self-hosted JS, worker, WASM and language data, including virtual directory.');
        for(const [width,height] of [[320,740],[390,844],[1280,900]]) {
            await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<500});
            assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'page overflow '+width);
            const shot=await send('Page.captureScreenshot',{format:'png'});
            fs.writeFileSync('artifacts/ocr-check/ocr-'+width+'.png',Buffer.from(shot.data,'base64'));
        }
        await evaluate('document.querySelector("[data-ocr-use-date]").click();document.querySelector("[data-ocr-use-amount]").click()');
        assert.deepEqual(await evaluate('[document.querySelector("#Date").value,document.querySelector("#Amount").value]'),['2026-11-10','1234,50']);


        // Cancellation also tears down a real controller during initialization.
        await evaluate('document.querySelector("[data-receipt-read]").click();document.querySelector("[data-ocr-cancel]").click()');
        assert.ok(await evaluate('document.querySelector("[data-ocr-status]").textContent.includes("avbröts")'));
        let workers=[];
        for(let attempt=0;attempt<60;attempt++){
            workers=(await send('Target.getTargets',{},null)).targetInfos.filter(info=>info.type==='worker' && info.url.includes('/vasen/'));
            if(!workers.length)break;
            await new Promise(resolve=>setTimeout(resolve,50));
        }
        assert.equal(workers.length,0,'real OCR workers stopped after cancellation');
        // Control worker messages only for race/failure tests; real OCR ran above.
        await evaluate(`window.terminated=0;window.NativeWorker=window.Worker;
            window.Worker=class {
                postMessage(){window.finishOcr=()=>this.onmessage?.({data:{type:'result',text:window.testOcrText || 'Datum 2026-01-02\\nTOTALT 99,00'}})}
                terminate(){window.terminated++}
            };document.querySelector('[data-receipt-read]').click();`);
        await wait('typeof window.finishOcr==="function"');
        await evaluate('document.querySelector("[data-ocr-cancel]").click();window.finishOcr()');
        assert.equal(await evaluate('document.querySelector("[data-ocr-results]").hidden'),true,'cancel hides suggestions');
        assert.equal(await evaluate('document.querySelector("[data-receipt-read]").disabled'),false,'cancel releases controls');
        await evaluate('window.finishOcr=null;document.querySelector("[data-receipt-read]").click()');
        await wait('typeof window.finishOcr==="function"');
        // Changing selection invalidates the old scan; late results must not reappear.
        await evaluate(`window.oldFinish=window.finishOcr;const dt=new DataTransfer();dt.items.add(new File(['%PDF-test'],'other.pdf',{type:'application/pdf'}));document.querySelector('#Uploads').files=dt.files;document.querySelector('#Uploads').dispatchEvent(new Event('change'));window.oldFinish();`);
        assert.equal(await evaluate('document.querySelector("#receipt-ocr").hidden'),true);
        assert.equal(await evaluate('document.querySelectorAll("[data-receipt-read]").length'),0);
        assert.equal(await evaluate('document.querySelector("#Amount").value'),'1234,50');
        await evaluate('chooseReceipt()');
        await wait('document.querySelector("#receipt-crop").open && !document.querySelector("#crop-apply").disabled');
        await evaluate('document.querySelector("#crop-original").click()');
        await evaluate(`window.testOcrText='TOTALT 100,00\\nTOTALT 200,00';document.querySelector('[data-receipt-read]').click();window.finishOcr();`);
        await wait('!document.querySelector("#receipt-ocr").hasAttribute("aria-busy")');
        assert.equal(await evaluate('document.querySelector("[data-ocr-amount]").value'),'','ambiguous total is not preselected');
        assert.equal(await evaluate('document.querySelector("[data-ocr-use-amount]").disabled'),true);
        await evaluate(`window.Worker=class {constructor(){throw Error('Offline')}};document.querySelector('[data-receipt-read]').click();`);
        await wait('!document.querySelector("#receipt-ocr").hasAttribute("aria-busy")');
        assert.ok(await evaluate('document.querySelector("[data-ocr-status]").textContent.includes("kunde inte")'));
        assert.equal(await evaluate('document.querySelector("#Amount").value'),'1234,50');

        // Timeout must release controls and preserve manually entered values.
        await evaluate(`window.Worker=class {postMessage(){} terminate(){window.terminated++}};
            window.NativeTimeout=window.setTimeout;
            window.setTimeout=(fn,delay,...args)=>window.NativeTimeout(fn,delay===90000?20:delay,...args);
            document.querySelector('[data-receipt-read]').click();`);
        await wait('!document.querySelector("#receipt-ocr").hasAttribute("aria-busy")');
        assert.ok(await evaluate('document.querySelector("[data-ocr-status]").textContent.includes("för lång tid")'));
        await evaluate('window.setTimeout=window.NativeTimeout');
        assert.ok(await evaluate('window.terminated>=3'),'cancel, change and timeout terminate workers');
        assert.equal(await evaluate('document.querySelector("#Amount").value'),'1234,50');
        assert.equal(issues.filter(s=>!s.includes('favicon.ico')).length,0,JSON.stringify(issues));
        assert.ok(requests.some(url=>url.endsWith('swe.traineddata.gz')));
        assert.ok(requests.some(url=>url.endsWith('eng.traineddata.gz')));
        console.log('PASS: explicit apply, untouched existing values, ambiguous totals, cancel, stale results, PDF, retry/failure, no CSP violations, 320/390/1280 layout.');
    } finally { await send('Browser.close',{},null).catch(()=>{}); socket.close(); server.close(); browser.kill(); }
})().catch(e=>{console.error(e);process.exitCode=1;server.close();});

