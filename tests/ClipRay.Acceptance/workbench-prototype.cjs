// Interaction rehearsal only: synthetic content, no production translation claims.
const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const path=require('node:path');
const fs=require('node:fs');
const {pathToFileURL}=require('node:url');
(async()=>{
 const output=path.resolve(process.argv[2]);fs.mkdirSync(output,{recursive:true});
 const browser=await chromium.launch({headless:true,...(process.env.CLIPRAY_PROTOTYPE_BROWSER?{executablePath:process.env.CLIPRAY_PROTOTYPE_BROWSER}:{})});
 try{
  const context=await browser.newContext({viewport:{width:1280,height:900}});
  const page=await context.newPage();
  await page.goto(pathToFileURL(path.resolve(__dirname,'../../docs/prototypes/translation-workbench.html')).href);
  const state=()=>page.evaluate(()=>window.prototypeState());
  const settle=()=>page.waitForFunction(()=>window.prototypeState().regions.every(r=>r.state!=='busy'));
  await page.locator('#draft').fill('Export starts in eight seconds.');
  await page.locator('#translate').click();await settle();
  let s=await state();assert.equal(s.requests,1);assert.equal(s.regions[0].state,'done');assert.equal(s.regions[1].state,'pending');
  await page.locator('#view').selectOption('translated');assert.equal((await state()).requests,1);
  await page.screenshot({path:path.join(output,'split.png'),fullPage:true});
  await page.locator('#layout').selectOption('focus');
  await page.screenshot({path:path.join(output,'focus.png'),fullPage:true});
  await page.locator('#layout').selectOption('split');
  await page.locator('summary').click();
  await page.locator('#fail').check();await page.locator('#translate').click();await settle();assert.equal((await state()).regions[0].state,'failed');
  await page.locator('#fail').uncheck();await page.locator('#overflow').check();await page.locator('#translate').click();await settle();s=await state();assert.equal(s.regions[0].state,'overflow');assert.ok(s.regions[0].result);assert.equal(await page.locator('#copy').isEnabled(),true);
  await page.locator('#overflow').uncheck();await page.locator('#translate').click();await page.locator('#cancel').click();await page.waitForTimeout(550);assert.equal((await state()).regions[0].state,'cancelled');
  await page.locator('#translate').click();await page.locator('#draft').fill('New draft during translation');await page.waitForTimeout(550);s=await state();assert.equal(s.regions[0].state,'modified');assert.equal(s.regions[0].result,'');
  await page.locator('#restore').click();assert.equal((await state()).regions[0].draft,'Export starts in e1ght seconds.');
  await page.locator('#imageRegions [data-region="1"]').click();assert.equal((await state()).selected,1);
  await page.locator('#skip').click();await page.locator('#all').click();await settle();s=await state();assert.equal(s.regions[1].state,'skipped');assert.equal(s.regions[2].state,'done');
  await page.setViewportSize({width:760,height:900});await page.screenshot({path:path.join(output,'narrow.png'),fullPage:true});
  const report={kind:'synthetic-interaction-rehearsal',checks:['single-region request','view switch no request','failure and retry','layout fallback preserves text','cancel rejects late completion','edit rejects late completion','restore OCR','image-list selection','skip in translate-all'],passed:true,foregroundUsed:false,productionEngine:false};
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
 }finally{await browser.close()}
})().catch(e=>{console.error(e);process.exitCode=1});
