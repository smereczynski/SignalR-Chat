#!/usr/bin/env node
const fs = require('fs');
const path = require('path');

function ensureDir(p){ if(!fs.existsSync(p)) fs.mkdirSync(p,{recursive:true}); }

const root = path.resolve(__dirname, '..');
const webrootLib = path.join(root, 'src','Chat.Web','wwwroot','lib');
ensureDir(webrootLib);

// Bootstrap copy (css + bundle js)
const bootstrapBase = path.join(root, 'node_modules','bootstrap','dist');
ensureDir(path.join(webrootLib,'bootstrap','css'));
ensureDir(path.join(webrootLib,'bootstrap','js'));
fs.copyFileSync(path.join(bootstrapBase,'css','bootstrap.min.css'), path.join(webrootLib,'bootstrap','css','bootstrap.min.css'));
fs.copyFileSync(path.join(bootstrapBase,'js','bootstrap.bundle.min.js'), path.join(webrootLib,'bootstrap','js','bootstrap.bundle.min.js'));

// SignalR copy (only browser dist)
const signalrBase = path.join(root,'node_modules','@microsoft','signalr','dist','browser');
ensureDir(path.join(webrootLib,'microsoft-signalr'));
for(const f of ['signalr.js','signalr.min.js']){
  fs.copyFileSync(path.join(signalrBase,f), path.join(webrootLib,'microsoft-signalr',f));
}

console.log('Libraries copied to wwwroot/lib');
