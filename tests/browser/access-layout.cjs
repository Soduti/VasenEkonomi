const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
process.chdir(root);
fs.mkdirSync('artifacts/access-check', { recursive: true });

const fixtures = new Set(['reader-list','reader-budget','reader-receipt','registrar-receipt','owner-member','owner-expenses','owner-expense-edit','owner-list']);
const server=http.createServer((req,res)=>{
    const route=decodeURIComponent(req.url.split('?')[0]);
    if(route==='/favicon.ico'){res.writeHead(204);res.end();return;}
    const name=route.slice(1);
    if(fixtures.has(name)){
        res.setHeader('Content-Type','text/html; charset=utf-8');
        res.end(fs.readFileSync('artifacts/access-check/'+name+'.html'));return;
    }
    const base=path.join(root,'src/Vasen.Web/wwwroot');
    const file=path.resolve(base,'.'+route);
    if(!file.startsWith(base+path.sep)||!fs.existsSync(file)||!fs.statSync(file).isFile()){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'application/octet-stream');
    res.end(fs.readFileSync(file));
});
(async() => {
    await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
    const port = server.address().port;
    const cache = path.join(process.env.LOCALAPPDATA || '', 'ms-playwright');
    const installed = fs.existsSync(cache) ? fs.readdirSync(cache).filter(name => /^chromium-\d+$/.test(name)).sort().reverse() : [];
    const chrome = process.env.CROP_TEST_CHROME || installed.map(name => path.join(cache, name, 'chrome-win64/chrome.exe')).find(file => fs.existsSync(file));
    if (!chrome) throw Error('Set CROP_TEST_CHROME to an installed Chromium/Chrome executable.');
    const browser = spawn(chrome,['--headless=new','--no-sandbox','--disable-gpu','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+path.join(root,'artifacts/access-check/profile3'),'about:blank'],{windowsHide:true,stdio:['ignore','ignore','pipe']});
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
        await send('Runtime.enable');await send('Log.enable');await send('Page.enable');await send('DOM.enable');await send('CSS.enable');
        for(const name of fixtures){
            for(const [width,height] of [[320,740],[390,844],[1280,900]]){
                await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<500});
                await send('Page.navigate',{url:'http://127.0.0.1:'+port+'/'+name});
                await wait('document.readyState==="complete"');
                assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'),true,name+' overflow '+width);
                assert.equal(await evaluate('document.querySelectorAll(".bottom-nav a").length'),name.startsWith('reader')?5:6,name+' nav');
                assert.equal(await evaluate('Array.from(document.querySelectorAll(".bottom-nav a")).some(a => a.getAttribute("href") === "/Expenses")'),true,name+' direct expenses link');
                assert.equal(await evaluate('Array.from(document.querySelectorAll(".bottom-nav a")).every(a => a.getBoundingClientRect().height >= 44 && a.scrollWidth <= a.clientWidth)'),true,name+' nav touch targets '+width);
                if(name.startsWith('owner-expense'))assert.equal(await evaluate('document.querySelector(".bottom-nav a.active").getAttribute("href")'),'/Expenses');
                if(name.startsWith('reader')){
                    assert.equal(await evaluate('document.querySelectorAll("input[type=file],input[name=ids],button[id=reimburse-button]").length'),0,name+' mutating controls');
                    assert.equal(await evaluate('Array.from(document.querySelectorAll("a")).some(a => a.getAttribute("href") === "/Receipts/Edit")'),false,name+' new receipt link');
                }
                if(name==='registrar-receipt')assert.equal(await evaluate('!!document.querySelector("input[type=file]")'),true);
                if(name==='owner-member')assert.equal(await evaluate('document.querySelector("#AccessLevel").options.length'),2);
                if(name==='owner-list'){
                    await evaluate('document.querySelector("input[name=ids]").click()');
                    assert.equal(await evaluate('document.querySelector("#reimburse-button").textContent'),'Registrera uttag/klar...');
                    const doc=await send('DOM.getDocument');
                    const {nodeId}=await send('DOM.querySelector',{nodeId:doc.root.nodeId,selector:'#reimburse-button'});
                    for(const state of ['hover','focus-visible']){
                        await send('CSS.forcePseudoState',{nodeId,forcedPseudoClasses:[state]});
                        assert.equal(await evaluate('(() => { const b=document.querySelector("#reimburse-button"),s=getComputedStyle(b); return s.color!==s.backgroundColor && s.backgroundColor!==getComputedStyle(b.closest(".selection-bar")).backgroundColor; })()'),true,'readable settlement button '+state+' '+width);
                    }
                    await send('CSS.forcePseudoState',{nodeId,forcedPseudoClasses:[]});
                }
                const shot=await send('Page.captureScreenshot',{format:'png'});
                fs.writeFileSync('artifacts/access-check/'+name+'-'+width+'.png',Buffer.from(shot.data,'base64'));
            }
        }
        assert.equal(issues.length,0,JSON.stringify(issues));
        console.log('PASS: real Razor snapshots for reader, registrar and owner at 320/390/1280px; role controls, navigation, no overflow or JavaScript errors.');
    } finally { await send('Browser.close',{},null).catch(()=>{}); socket.close(); server.close(); browser.kill(); }
})().catch(e=>{console.error(e);process.exitCode=1;server.close();});

