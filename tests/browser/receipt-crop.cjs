const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
process.chdir(root);
fs.mkdirSync('artifacts/crop-check', { recursive: true });
const razor = fs.readFileSync('src/Vasen.Web/Pages/Receipts/Edit.cshtml','utf8');
const dialog = razor.match(/    <dialog[\s\S]*?<\/dialog>/)[0];
const html = '<!doctype html><html lang="sv"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/css/site.css"><body><main class="page-shell"><div class="receipts-page receipt-edit-page narrow"><form class="panel receipt-edit-form"><input type="file" id="Uploads" name="Uploads" multiple><div id="file-previews"></div>' + dialog + '<button id="save">Spara utlägg</button></form></div></main><script src="/js/receipt-crop.js" defer></script></body></html>';
const server = http.createServer((req,res) => {
    res.setHeader('Content-Security-Policy',"default-src 'self'; img-src 'self' blob:; style-src 'self'; script-src 'self'; object-src 'none'");
    if(req.url === '/') { res.setHeader('Content-Type','text/html; charset=utf-8'); res.end(html); }
    else {
        const relative = req.url === '/js/receipt-crop.js' ? 'js/receipt-crop.js' : 'css/site.css';
        res.setHeader('Content-Type', relative.endsWith('.js') ? 'text/javascript' : 'text/css');
        res.end(fs.readFileSync(path.join(root,'src/Vasen.Web/wwwroot',relative)));
    }
});
(async() => {
    await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
    const port = server.address().port;
    const cache = path.join(process.env.LOCALAPPDATA || '', 'ms-playwright');
    const installed = fs.existsSync(cache) ? fs.readdirSync(cache).filter(name => /^chromium-\d+$/.test(name)).sort().reverse() : [];
    const chrome = process.env.CROP_TEST_CHROME || installed.map(name => path.join(cache, name, 'chrome-win64/chrome.exe')).find(file => fs.existsSync(file));
    if (!chrome) throw Error('Set CROP_TEST_CHROME to an installed Chromium/Chrome executable.');
    const browser = spawn(chrome,['--headless=new','--no-sandbox','--disable-gpu','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+path.join(root,'artifacts/crop-check/profile3'),'about:blank'],{windowsHide:true,stdio:['ignore','ignore','pipe']});
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
        const id=++serial; const timer=setTimeout(()=>{pending.delete(id);reject(Error('Protocol timeout '+method))},15000); pending.set(id,{resolve:v=>{clearTimeout(timer);resolve(v)},reject:e=>{clearTimeout(timer);reject(e)}});
        socket.send(JSON.stringify({id,method,params,...(sid?{sessionId:sid}:{})}));
    });
    const evaluate=async(expression)=> {
        const result=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
        if(result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
        return result.result.value;
    };
    const wait=async(expression)=> { for(let i=0;i<100;i++){if(await evaluate(expression))return;await new Promise(r=>setTimeout(r,30));}throw Error('Timed out: '+expression); };
    try {
        const target=await send('Target.createTarget',{url:'about:blank'},null);
        session=(await send('Target.attachToTarget',{targetId:target.targetId,flatten:true},null)).sessionId;
        await send('Runtime.enable'); await send('Log.enable'); await send('Page.enable');
        await send('Emulation.setDeviceMetricsOverride',{width:320,height:740,deviceScaleFactor:1,mobile:true});
        await send('Page.navigate',{url:'http://127.0.0.1:'+port});
        await wait('!!document.querySelector("#Uploads")');
        await evaluate(`window.choose = async function() {
            const canvas=document.createElement('canvas');canvas.width=400;canvas.height=600;
            const c=canvas.getContext('2d');c.fillStyle='#f00';c.fillRect(0,0,400,600);
            c.fillStyle='#0f0';c.fillRect(100,150,200,300);
            const blob=await new Promise(r=>canvas.toBlob(r,'image/png'));
            const transfer=new DataTransfer();transfer.items.add(new File([blob],'receipt.png',{type:'image/png'}));
            transfer.items.add(new File(['%PDF-test'],'receipt.pdf',{type:'application/pdf'}));
            transfer.items.add(new File([blob],'second.png',{type:'image/png'}));
            document.querySelector('#Uploads').files=transfer.files;
            document.querySelector('#Uploads').dispatchEvent(new Event('change'));
        }; choose();`);
        await wait('document.querySelector("#receipt-crop").open && !document.querySelector("#crop-apply").disabled');
        assert.equal(await evaluate('document.documentElement.scrollWidth <= innerWidth'),true);
        // Drag the upper left corner using genuine touch input at phone width.
        let box=await evaluate('(()=>{const r=document.querySelector("#crop-canvas").getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height}})()');
        await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:box.x+2,y:box.y+2,id:1}]});
        await send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:box.x+box.w*.2,y:box.y+box.h*.2,id:1}]});
        await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        assert.equal(await evaluate('Number(document.querySelector("#crop-left").value) >= 18'),true,'touch resize');
        // Set exact crop for pixel and dimension validation.
        await evaluate(`document.querySelector('.crop-precision').open=true;
            for(const [edge,value] of [['left',25],['top',25],['right',75],['bottom',75]]) {
                const input=document.querySelector('#crop-'+edge);input.value=value;input.dispatchEvent(new Event('change'));
            }`);
        for(const [width,height] of [[320,740],[390,844],[1280,900]]) {
            await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<500});
            assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'),true,'page overflow '+width);
            assert.equal(await evaluate('(()=>{const d=document.querySelector("#receipt-crop"); return d.scrollWidth<=d.clientWidth})()'),true,'dialog overflow '+width);
            const shot=await send('Page.captureScreenshot',{format:'png'});
            fs.writeFileSync('artifacts/crop-check/crop-'+width+'.png',Buffer.from(shot.data,'base64'));
        }
        await evaluate('document.querySelector("#crop-apply").click()');
        await wait('!document.querySelector("#receipt-crop").open');
        const result=await evaluate(`(async()=>{const input=document.querySelector('#Uploads');const files=[...new FormData(input.form).values()];
            const f=new FormData(input.form).get('Uploads'),bitmap=await createImageBitmap(f),c=document.createElement('canvas');c.width=bitmap.width;c.height=bitmap.height;
            c.getContext('2d').drawImage(bitmap,0,0);return {width:bitmap.width,height:bitmap.height,pixel:[...c.getContext('2d').getImageData(50,50,1,1).data],name:f.name,type:f.type,count:input.files.length,pdf:await input.files[1].text(),third:input.files[2].name}})()`);
        assert.equal(result.width,200);assert.equal(result.height,300);assert.equal(result.name,'receipt-beskuren.jpg');
        assert.equal(result.type,'image/jpeg'); assert.equal(result.count,3);assert.equal(result.pdf,'%PDF-test');assert.equal(result.third,'second.png');
        assert.ok(result.pixel[1]>240 && result.pixel[0]<15,'cropped pixel content');
        const submits=await evaluate(`(()=>{let submitted=false;const form=document.querySelector('form');form.addEventListener('submit',e=>{submitted=!e.defaultPrevented;e.preventDefault()},{once:true});form.requestSubmit();return submitted})()`);
        assert.equal(submits,true,'save is no longer blocked after applying');
        // Reopening starts from the original; cancellation preserves the prior crop.
        await evaluate('document.querySelector("#file-previews button").click()');
        await wait('!document.querySelector("#crop-apply").disabled');
        assert.equal(await evaluate('document.querySelector("#crop-left").value'),'0');
        // Keyboard values can be applied without Enter submitting the surrounding receipt form.
        await evaluate('document.querySelector(".crop-precision").open=true;document.querySelector("#crop-left").value=10;document.querySelector("#crop-left").dispatchEvent(new KeyboardEvent("keydown",{key:"Enter",bubbles:true,cancelable:true}))');
        assert.equal(await evaluate('document.querySelector("#crop-left").value'),'10');
        await evaluate('document.querySelector("#crop-reset").click()');
        assert.equal(await evaluate('document.querySelector("#crop-left").value'),'0');
        await evaluate('document.querySelector("#crop-cancel").click()');
        assert.equal(await evaluate('document.querySelector("#Uploads").files[0].name'),'receipt-beskuren.jpg');
        await evaluate('document.querySelector("#file-previews button").click()');
        await wait('!document.querySelector("#crop-apply").disabled');
        await evaluate('document.querySelector("#crop-rotate").click();document.querySelector("#crop-apply").click()');
        await wait('!document.querySelector("#receipt-crop").open');
        assert.deepEqual(await evaluate('(async()=>{const b=await createImageBitmap(document.querySelector("#Uploads").files[0]);return [b.width,b.height]})()'),[600,400]);
        await evaluate('document.querySelector("#file-previews button").click()');
        await wait('!document.querySelector("#crop-apply").disabled');
        await evaluate('document.querySelector("#crop-original").click()');
        assert.equal(await evaluate('document.querySelector("#Uploads").files[0].name'),'receipt.png');
        // Second image is independent.
        await evaluate('document.querySelectorAll("#file-previews button")[1].click()');
        await wait('!document.querySelector("#crop-apply").disabled');
        await evaluate('document.querySelector("#crop-apply").click()');
        await wait('!document.querySelector("#receipt-crop").open');
        assert.equal(await evaluate('document.querySelector("#Uploads").files[2].name'),'second-beskuren.jpg');
        // Corrupt image fails gracefully, with original still available.
        await evaluate(`const dt=new DataTransfer();dt.items.add(new File(['bad'],'bad.jpg',{type:'image/jpeg'}));document.querySelector('#Uploads').files=dt.files;document.querySelector('#Uploads').dispatchEvent(new Event('change'));`);
        await wait('!!document.querySelector("#crop-error").textContent');
        assert.equal(await evaluate('document.querySelector("#crop-apply").disabled'),true);
        await evaluate('document.querySelector("#crop-original").click()');
        // Ignore the expected browser decoding error for the deliberately corrupt thumbnail.
        assert.equal(issues.filter(s=>!s.includes('ERR_')).length,0,JSON.stringify(issues));
        console.log('PASS: touch crop, exact JPEG pixels/dimensions, rotation, originals, cancel, multiple files/PDF, native save, corrupt image, CSP and widths 320/390/1280.');
    } finally { await send('Browser.close',{},null).catch(()=>{}); socket.close(); server.close(); browser.kill(); }
})().catch(e=>{console.error(e);process.exitCode=1;server.close();});




