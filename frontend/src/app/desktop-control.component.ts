import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppAudioSession,
  CommandResult,
  ControlService,
  DesktopProcess,
  DesktopRuntime,
  DesktopWindow,
  HomeCardPreference,
  SettingsEnvelope
} from './core/control.service';

type DesktopTab='windows'|'tasks'|'mixer'|'remote'|'layout';
type FeedbackState='success'|'warning'|'error';
type StreamPreset='max'|'balanced'|'quality';
type TwoFingerGesture='none'|'pending'|'pinch'|'scroll';
interface DesktopMonitor{ id:string;name:string;left:number;top:number;width:number;height:number;primary:boolean;screenCount:number; }
interface PointerPoint{ x:number;y:number;startX:number;startY:number;clientX:number;clientY:number;startClientX:number;startClientY:number;startedAt:number; }

@Component({
  selector:'ta-desktop-control',
  standalone:true,
  imports:[CommonModule,FormsModule],
  templateUrl:'./desktop-control.component.html',
  styleUrl:'./desktop-control.component.css'
})
export class DesktopControlComponent implements OnInit,OnDestroy {
  @ViewChild('remoteCanvas') remoteCanvas?:ElementRef<HTMLCanvasElement>;

  readonly runtime=signal<DesktopRuntime|null>(null);
  readonly windows=signal<DesktopWindow[]>([]);
  readonly processes=signal<DesktopProcess[]>([]);
  readonly audioSessions=signal<AppAudioSession[]>([]);
  readonly monitors=signal<DesktopMonitor[]>([]);
  readonly settings=signal<SettingsEnvelope|null>(null);
  readonly feedback=signal<{state:FeedbackState;title:string;detail:string}|null>(null);
  readonly loading=signal(false);
  readonly streamState=signal<'off'|'connecting'|'live'|'error'>('off');
  readonly streamFps=signal(0);
  readonly streamLatency=signal(0);
  readonly streamWidth=signal(0);
  readonly streamHeight=signal(0);
  readonly remoteZoom=signal(1);
  readonly remoteExpanded=signal(false);
  readonly zoomToolsVisible=signal(false);

  tab:DesktopTab='windows';
  windowFilter='';
  processFilter='';
  remoteText='';
  remoteMonitor='all';
  remotePreset:StreamPreset='max';

  private pollTimer?:number;
  private ws?:WebSocket;
  private reconnectTimer?:number;
  private pendingFrame?:ArrayBuffer;
  private rendering=false;
  private frameCounter=0;
  private frameWindowStart=performance.now();
  private refreshing=false;
  private readonly pointers=new Map<number,PointerPoint>();
  private dragging=false;
  private dragStart?:{x:number;y:number};
  private lastMoveAt=0;
  private longPressTimer?:number;
  private longPressTriggered=false;
  private lastTapAt=0;
  private lastTapPoint?:{x:number;y:number};
  private singleTapTimer?:number;
  private pinchStartDistance=0;
  private pinchStartZoom=1;
  private twoFingerLastY=0;
  private twoFingerStartCenterY=0;
  private twoFingerGesture:TwoFingerGesture='none';
  private zoomHideTimer?:number;

  constructor(private readonly control:ControlService){}

  async ngOnInit():Promise<void>{
    await this.refreshEverything();
    this.pollTimer=window.setInterval(()=>void this.pollVisibleState(),1600);
  }

  ngOnDestroy():void{
    if(this.pollTimer)window.clearInterval(this.pollTimer);
    this.stopStream();
    this.clearGestureTimers();
    document.body.classList.remove('twina-remote-expanded');
  }

  setTab(tab:DesktopTab):void{
    this.tab=tab;
    if(tab==='remote')queueMicrotask(()=>this.startStream());else this.stopStream();
    void this.pollVisibleState(true);
  }

  filteredWindows():DesktopWindow[]{
    const q=this.windowFilter.trim().toLowerCase();
    if(!q)return this.windows();
    return this.windows().filter(w=>`${w.title} ${w.processName} ${w.pid}`.toLowerCase().includes(q));
  }

  filteredProcesses():DesktopProcess[]{
    const q=this.processFilter.trim().toLowerCase();
    if(!q)return this.processes();
    return this.processes().filter(p=>`${p.name} ${p.windowTitle} ${p.pid}`.toLowerCase().includes(q));
  }

  async refreshEverything():Promise<void>{
    this.loading.set(true);
    try{
      await Promise.allSettled([this.refreshRuntime(),this.refreshWindows(),this.refreshProcesses(),this.refreshAudio(),this.refreshSettings(),this.refreshMonitors()]);
    }finally{this.loading.set(false);}
  }

  async windowAction(item:DesktopWindow,action:'focus'|'minimize'|'maximize'|'restore'|'close'):Promise<void>{
    if(action==='close'&&!confirm(`Close '${item.title}'? Unsaved work may trigger a confirmation window on the PC.`))return;
    try{
      const result=await this.control.post<CommandResult>('/api/desktop/window/action',{handle:item.handle,action});
      this.resultFeedback(result,`${action} verified`);
    }catch(e){this.fail('Window action failed',e);}
    await Promise.allSettled([this.refreshWindows(),this.refreshRuntime(),this.refreshProcesses()]);
  }

  async endProcess(item:DesktopProcess):Promise<void>{
    if(item.protected){this.notice('Protected task',`${item.name} (${item.pid}) cannot be ended from TWIN A.`,'warning');return;}
    const label=item.windowTitle?`${item.name} — ${item.windowTitle}`:item.name;
    if(!confirm(`End task '${label}' (PID ${item.pid})?\n\nUnsaved data in this process can be lost.`))return;
    try{
      const result=await this.control.post<CommandResult>('/api/desktop/process/end',{pid:item.pid});
      this.resultFeedback(result,'Task ended and verified');
    }catch(e){this.fail('End task failed',e);}
    await Promise.allSettled([this.refreshProcesses(),this.refreshWindows(),this.refreshRuntime(),this.refreshAudio()]);
  }

  async setSessionVolume(item:AppAudioSession,event:Event):Promise<void>{
    const volume=Number((event.target as HTMLInputElement).value);
    try{
      const result=await this.control.post<CommandResult>('/api/desktop/audio-session',{pid:item.pid,volume,muted:null});
      this.resultFeedback(result,`${item.processName} volume verified`);
      await this.refreshAudio();
    }catch(e){this.fail('App volume failed',e);await this.refreshAudio();}
  }

  async toggleSessionMute(item:AppAudioSession):Promise<void>{
    try{
      const result=await this.control.post<CommandResult>('/api/desktop/audio-session',{pid:item.pid,volume:null,muted:!item.muted});
      this.resultFeedback(result,`${item.processName} mute state verified`);
      await this.refreshAudio();
    }catch(e){this.fail('App mute failed',e);await this.refreshAudio();}
  }

  remoteControlEnabled():boolean{return this.settings()?.settings.ui.enableRemoteControl??false;}

  async changeRemoteControl(enabled:boolean):Promise<void>{
    const envelope=this.settings();if(!envelope)return;
    if(enabled&&!confirm('Enable remote mouse and keyboard control?\n\nWhile enabled, anyone who can access this TWIN A instance through your permitted network path can send input to the logged-in PC.'))return;
    envelope.settings.ui.enableRemoteControl=enabled;
    try{
      await this.control.put('/api/settings',envelope.settings);
      await this.refreshSettings();
      this.notice(enabled?'Remote control enabled':'Remote control disabled',enabled?'Mouse, touch gestures and keyboard shortcuts are enabled. Input remains state-unverified because Windows cannot prove what an application did with each input event.':'Remote desktop remains view-only.','success');
    }catch(e){this.fail('Remote-control setting failed',e);await this.refreshSettings();}
  }

  changeMonitor():void{this.resetZoom(false);this.startStream();}
  changePreset():void{this.startStream();}
  resetZoom(show=true):void{this.remoteZoom.set(1);if(show)this.showZoomTools();}
  zoomIn():void{this.remoteZoom.set(Math.min(3,Math.round((this.remoteZoom()+0.25)*100)/100));this.showZoomTools();}
  zoomOut():void{this.remoteZoom.set(Math.max(1,Math.round((this.remoteZoom()-0.25)*100)/100));this.showZoomTools();}
  toggleZoomTools():void{this.zoomToolsVisible()?this.hideZoomTools():this.showZoomTools(3000);}

  toggleExpanded():void{
    const next=!this.remoteExpanded();
    this.remoteExpanded.set(next);
    document.body.classList.toggle('twina-remote-expanded',next);
    queueMicrotask(()=>this.focusRemoteCanvas());
  }

  presetLabel():string{
    return this.remotePreset==='max'?'MAX FPS · 60 target':this.remotePreset==='balanced'?'BALANCED · 45 target':'HIGH QUALITY · 30 target';
  }

  selectedMonitor():DesktopMonitor|undefined{return this.monitors().find(m=>m.id===this.remoteMonitor);}

  remotePointerDown(event:PointerEvent):void{
    event.preventDefault();
    const canvas=event.currentTarget as HTMLElement;
    canvas.setPointerCapture?.(event.pointerId);
    const pos=this.pointerPosition(event);
    this.pointers.set(event.pointerId,{...pos,startX:pos.x,startY:pos.y,clientX:event.clientX,clientY:event.clientY,startClientX:event.clientX,startClientY:event.clientY,startedAt:performance.now()});

    if(this.pointers.size===1){
      this.longPressTriggered=false;
      this.dragStart=pos;
      if(this.remoteControlEnabled()){
        if(this.longPressTimer)window.clearTimeout(this.longPressTimer);
        this.longPressTimer=window.setTimeout(()=>{
          const point=this.pointers.get(event.pointerId);
          if(!point||this.dragging||this.pointers.size!==1||!this.remoteControlEnabled())return;
          this.longPressTriggered=true;
          void this.sendInput({action:'rightclick',...this.pointerCoordinates(point.x,point.y)},false);
        },520);
      }
    }else if(this.pointers.size===2){
      this.cancelLongPress();
      const pair=[...this.pointers.values()].slice(0,2);
      this.pinchStartDistance=this.pixelDistance(pair[0],pair[1]);
      this.pinchStartZoom=this.remoteZoom();
      this.twoFingerLastY=(pair[0].clientY+pair[1].clientY)/2;
      this.twoFingerStartCenterY=this.twoFingerLastY;
      this.twoFingerGesture='pending';
      if(this.dragging&&this.remoteControlEnabled()){
        this.dragging=false;
        const p=pair[0];
        void this.sendInput({action:'leftup',...this.pointerCoordinates(p.x,p.y)},false);
      }
    }
  }

  remotePointerMove(event:PointerEvent):void{
    const point=this.pointers.get(event.pointerId);if(!point)return;
    event.preventDefault();
    const pos=this.pointerPosition(event);point.x=pos.x;point.y=pos.y;point.clientX=event.clientX;point.clientY=event.clientY;

    if(this.pointers.size>=2){
      this.cancelLongPress();
      const pair=[...this.pointers.values()].slice(0,2);
      const distance=this.pixelDistance(pair[0],pair[1]);
      const centerY=(pair[0].clientY+pair[1].clientY)/2;
      const distanceRatio=this.pinchStartDistance>0?distance/this.pinchStartDistance:1;
      const pinchDelta=Math.abs(distanceRatio-1);
      const centerTravel=Math.abs(centerY-this.twoFingerStartCenterY);

      if(this.twoFingerGesture==='pending'){
        if(pinchDelta>=0.035)this.twoFingerGesture='pinch';
        else if(centerTravel>=18)this.twoFingerGesture='scroll';
      }

      if(this.twoFingerGesture==='pinch'){
        const zoom=Math.max(1,Math.min(3,this.pinchStartZoom*distanceRatio));
        this.remoteZoom.set(Math.round(zoom*100)/100);
        this.showZoomTools();
        return;
      }

      if(this.twoFingerGesture==='scroll'&&this.remoteControlEnabled()){
        const dy=centerY-this.twoFingerLastY;
        if(Math.abs(dy)>10){
          this.twoFingerLastY=centerY;
          const centerX=(pair[0].x+pair[1].x)/2;
          const normalizedY=(pair[0].y+pair[1].y)/2;
          void this.sendInput({action:'wheel',delta:dy>0?120:-120,...this.pointerCoordinates(centerX,normalizedY)},false);
        }
      }
      return;
    }

    if(!this.remoteControlEnabled())return;
    const moved=Math.hypot(point.clientX-point.startClientX,point.clientY-point.startClientY);
    if(moved>10)this.cancelLongPress();
    if(moved>10&&!this.dragging){
      this.dragging=true;
      const start=this.dragStart??{x:point.startX,y:point.startY};
      void this.sendInput({action:'leftdown',...this.pointerCoordinates(start.x,start.y)},false);
    }
    if(this.dragging){
      const now=performance.now();if(now-this.lastMoveAt<28)return;this.lastMoveAt=now;
      void this.sendInput({action:'move',...this.pointerCoordinates(point.x,point.y)},false);
    }
  }

  remotePointerUp(event:PointerEvent):void{
    const point=this.pointers.get(event.pointerId);if(!point)return;
    event.preventDefault();
    const wasMulti=this.pointers.size>1;
    this.pointers.delete(event.pointerId);
    this.cancelLongPress();

    if(wasMulti){
      if(this.pointers.size<2){
        this.pinchStartDistance=0;
        this.twoFingerLastY=0;
        this.twoFingerStartCenterY=0;
        this.twoFingerGesture='none';
      }
      return;
    }

    if(!this.remoteControlEnabled())return;
    const coords=this.pointerCoordinates(point.x,point.y);
    if(this.dragging){
      this.dragging=false;
      void this.sendInput({action:'leftup',...coords},false);
      return;
    }
    if(this.longPressTriggered){this.longPressTriggered=false;return;}

    const now=performance.now();
    const isDouble=now-this.lastTapAt<320&&this.lastTapPoint&&Math.hypot(point.x-this.lastTapPoint.x,point.y-this.lastTapPoint.y)<0.035;
    if(isDouble){
      if(this.singleTapTimer)window.clearTimeout(this.singleTapTimer);
      this.singleTapTimer=undefined;this.lastTapAt=0;this.lastTapPoint=undefined;
      void this.sendInput({action:'doubleclick',...coords},false);
    }else{
      this.lastTapAt=now;this.lastTapPoint={x:point.x,y:point.y};
      if(this.singleTapTimer)window.clearTimeout(this.singleTapTimer);
      this.singleTapTimer=window.setTimeout(()=>{
        void this.sendInput({action:'leftclick',...coords},false);
        this.singleTapTimer=undefined;
      },250);
    }
  }

  remotePointerCancel(event:PointerEvent):void{
    const point=this.pointers.get(event.pointerId);
    this.pointers.delete(event.pointerId);this.cancelLongPress();
    if(this.pointers.size<2){this.twoFingerGesture='none';this.pinchStartDistance=0;}
    if(this.dragging&&point&&this.remoteControlEnabled()){this.dragging=false;void this.sendInput({action:'leftup',...this.pointerCoordinates(point.x,point.y)},false);}
  }

  remoteContextMenu(event:MouseEvent):void{
    event.preventDefault();if(!this.remoteControlEnabled())return;
    const pos=this.pointerPosition(event);void this.sendInput({action:'rightclick',...this.pointerCoordinates(pos.x,pos.y)},false);
  }

  remoteWheel(event:WheelEvent):void{
    if(!this.remoteControlEnabled())return;
    event.preventDefault();const pos=this.pointerPosition(event);const delta=event.deltaY>0?-120:120;
    void this.sendInput({action:'wheel',delta,...this.pointerCoordinates(pos.x,pos.y)},false);
  }

  async remoteKey(key:string):Promise<void>{await this.sendInput({action:'key',key,monitorId:this.remoteMonitor},true);}
  async remoteShortcut(shortcut:string):Promise<void>{await this.sendInput({action:'shortcut',shortcut,monitorId:this.remoteMonitor},true);}
  async sendRemoteText():Promise<void>{
    const text=this.remoteText;if(!text)return;
    await this.sendInput({action:'text',text,monitorId:this.remoteMonitor},true);this.remoteText='';
  }

  homeCards():HomeCardPreference[]{return [...(this.settings()?.settings.ui.homeCards??[])].sort((a,b)=>a.order-b.order);}
  async toggleHomeCard(item:HomeCardPreference):Promise<void>{item.visible=!item.visible;await this.saveLayout(false);}
  async toggleCardSize(item:HomeCardPreference):Promise<void>{item.size=item.size==='wide'?'normal':'wide';await this.saveLayout(false);}
  async moveHomeCard(item:HomeCardPreference,direction:-1|1):Promise<void>{
    const envelope=this.settings();if(!envelope)return;
    const cards=[...envelope.settings.ui.homeCards].sort((a,b)=>a.order-b.order);
    const index=cards.findIndex(c=>c.key===item.key);const target=index+direction;
    if(index<0||target<0||target>=cards.length)return;
    [cards[index],cards[target]]=[cards[target],cards[index]];
    cards.forEach((card,i)=>card.order=(i+1)*10);envelope.settings.ui.homeCards=cards;
    await this.saveLayout(false);
  }
  async resetHomeLayout():Promise<void>{
    if(!confirm('Restore the default Home dashboard card order, visibility and sizes?'))return;
    const envelope=this.settings();if(!envelope)return;envelope.settings.ui.homeCards=[];await this.saveLayout(true);
  }

  formatBytes(n:number):string{if(!Number.isFinite(n))return'—';const u=['B','KB','MB','GB','TB'];let i=0;while(n>=1024&&i<u.length-1){n/=1024;i++;}return`${n<10&&i>0?n.toFixed(1):Math.round(n)} ${u[i]}`;}
  windowState(item:DesktopWindow):string{if(item.foreground)return'FOREGROUND';if(item.minimized)return'MINIMIZED';if(item.maximized)return'MAXIMIZED';return'OPEN';}

  private async pollVisibleState(force=false):Promise<void>{
    if(this.refreshing&&!force)return;this.refreshing=true;
    try{
      const jobs:Promise<unknown>[]=[this.refreshRuntime()];
      if(this.tab==='windows')jobs.push(this.refreshWindows());
      if(this.tab==='tasks')jobs.push(this.refreshProcesses());
      if(this.tab==='mixer')jobs.push(this.refreshAudio());
      if(this.tab==='layout')jobs.push(this.refreshSettings());
      await Promise.allSettled(jobs);
    }finally{this.refreshing=false;}
  }

  private async refreshRuntime():Promise<void>{try{this.runtime.set(await this.control.get<DesktopRuntime>('/api/desktop/runtime'));}catch{this.runtime.set(null);}}
  private async refreshWindows():Promise<void>{try{this.windows.set(await this.control.get<DesktopWindow[]>('/api/desktop/windows'));}catch{this.windows.set([]);}}
  private async refreshProcesses():Promise<void>{try{this.processes.set(await this.control.get<DesktopProcess[]>('/api/desktop/processes'));}catch{this.processes.set([]);}}
  private async refreshAudio():Promise<void>{try{this.audioSessions.set(await this.control.get<AppAudioSession[]>('/api/desktop/audio-sessions'));}catch{this.audioSessions.set([]);}}
  private async refreshSettings():Promise<void>{try{this.settings.set(await this.control.get<SettingsEnvelope>('/api/settings'));}catch{}}
  private async refreshMonitors():Promise<void>{
    try{
      const monitors=await this.control.get<DesktopMonitor[]>('/api/desktop/monitors');this.monitors.set(monitors);
      if(!monitors.some(m=>m.id===this.remoteMonitor))this.remoteMonitor=monitors[0]?.id??'all';
    }catch{this.monitors.set([]);}
  }

  private streamConfig():{maxWidth:number;quality:number;fps:number}{
    if(this.remotePreset==='quality')return{maxWidth:2560,quality:78,fps:30};
    if(this.remotePreset==='balanced')return{maxWidth:1920,quality:66,fps:45};
    return{maxWidth:1600,quality:56,fps:60};
  }

  private startStream():void{
    this.stopStream(false);
    if(this.tab!=='remote')return;
    const config=this.streamConfig();
    const scheme=location.protocol==='https:'?'wss':'ws';
    const url=`${scheme}://${location.host}/ws/desktop?monitorId=${encodeURIComponent(this.remoteMonitor)}&maxWidth=${config.maxWidth}&quality=${config.quality}&fps=${config.fps}`;
    this.streamState.set('connecting');this.streamFps.set(0);this.frameCounter=0;this.frameWindowStart=performance.now();
    const ws=new WebSocket(url);this.ws=ws;ws.binaryType='arraybuffer';
    ws.onopen=()=>{if(this.ws===ws)this.streamState.set('live');};
    ws.onmessage=(event)=>{
      if(!(event.data instanceof ArrayBuffer))return;
      this.pendingFrame=event.data;
      if(!this.rendering)void this.renderPendingFrames();
    };
    ws.onerror=()=>{if(this.ws===ws)this.streamState.set('error');};
    ws.onclose=()=>{
      if(this.ws!==ws)return;this.ws=undefined;
      if(this.tab==='remote'){
        this.streamState.set('error');
        this.reconnectTimer=window.setTimeout(()=>this.startStream(),850);
      }else this.streamState.set('off');
    };
  }

  private stopStream(clearState=true):void{
    if(this.reconnectTimer)window.clearTimeout(this.reconnectTimer);this.reconnectTimer=undefined;
    const ws=this.ws;this.ws=undefined;
    if(ws&&(ws.readyState===WebSocket.OPEN||ws.readyState===WebSocket.CONNECTING))ws.close();
    this.pendingFrame=undefined;this.rendering=false;
    if(clearState)this.streamState.set('off');
  }

  private async renderPendingFrames():Promise<void>{
    this.rendering=true;
    try{
      while(this.pendingFrame&&this.tab==='remote'){
        const packet=this.pendingFrame;this.pendingFrame=undefined;
        if(packet.byteLength<=16)continue;
        const view=new DataView(packet);
        const capturedAt=Number(view.getBigInt64(0,true));
        const width=view.getInt32(8,true);const height=view.getInt32(12,true);
        const jpeg=packet.slice(16);
        const canvas=this.remoteCanvas?.nativeElement;if(!canvas)continue;
        const blob=new Blob([jpeg],{type:'image/jpeg'});
        await this.drawBlob(canvas,blob,width,height);
        this.streamWidth.set(width);this.streamHeight.set(height);
        this.streamLatency.set(Math.max(0,Date.now()-capturedAt));
        this.frameCounter++;
        const now=performance.now();const elapsed=now-this.frameWindowStart;
        if(elapsed>=1000){this.streamFps.set(Math.round((this.frameCounter*1000/elapsed)*10)/10);this.frameCounter=0;this.frameWindowStart=now;}
      }
    }finally{this.rendering=false;if(this.pendingFrame&&this.tab==='remote')void this.renderPendingFrames();}
  }

  private async drawBlob(canvas:HTMLCanvasElement,blob:Blob,width:number,height:number):Promise<void>{
    const ctx=canvas.getContext('2d',{alpha:false});if(!ctx)return;
    if(canvas.width!==width)canvas.width=width;if(canvas.height!==height)canvas.height=height;
    if('createImageBitmap' in window){
      const bitmap=await createImageBitmap(blob);ctx.drawImage(bitmap,0,0,width,height);bitmap.close();return;
    }
    await new Promise<void>((resolve,reject)=>{
      const url=URL.createObjectURL(blob);const image=new Image();
      image.onload=()=>{ctx.drawImage(image,0,0,width,height);URL.revokeObjectURL(url);resolve();};
      image.onerror=()=>{URL.revokeObjectURL(url);reject(new Error('Frame decode failed.'));};image.src=url;
    });
  }

  private focusRemoteCanvas():void{this.remoteCanvas?.nativeElement.focus?.();}

  private pointerPosition(event:MouseEvent|PointerEvent|WheelEvent):{x:number;y:number}{
    const canvas=this.remoteCanvas?.nativeElement;const rect=canvas?.getBoundingClientRect();
    if(!rect||rect.width<=0||rect.height<=0)return{x:.5,y:.5};
    return{x:Math.max(0,Math.min(1,(event.clientX-rect.left)/rect.width)),y:Math.max(0,Math.min(1,(event.clientY-rect.top)/rect.height))};
  }

  private pointerCoordinates(x:number,y:number):{x:number;y:number;monitorId:string}{return{x,y,monitorId:this.remoteMonitor};}
  private pixelDistance(a:PointerPoint,b:PointerPoint):number{return Math.hypot(a.clientX-b.clientX,a.clientY-b.clientY);}
  private cancelLongPress():void{if(this.longPressTimer)window.clearTimeout(this.longPressTimer);this.longPressTimer=undefined;}
  private showZoomTools(duration=1600):void{
    this.zoomToolsVisible.set(true);
    if(this.zoomHideTimer)window.clearTimeout(this.zoomHideTimer);
    this.zoomHideTimer=window.setTimeout(()=>this.zoomToolsVisible.set(false),duration);
  }
  private hideZoomTools():void{if(this.zoomHideTimer)window.clearTimeout(this.zoomHideTimer);this.zoomHideTimer=undefined;this.zoomToolsVisible.set(false);}
  private clearGestureTimers():void{this.cancelLongPress();this.hideZoomTools();if(this.singleTapTimer)window.clearTimeout(this.singleTapTimer);this.singleTapTimer=undefined;}

  private async sendInput(payload:Record<string,unknown>,showFeedback:boolean):Promise<void>{
    if(!this.remoteControlEnabled()){if(showFeedback)this.notice('View-only mode','Enable remote screen control before sending input.','warning');return;}
    try{
      const result=await this.control.post<CommandResult>('/api/desktop/input',payload);
      if(showFeedback)this.resultFeedback(result,'Input sent');
    }catch(e){if(showFeedback)this.fail('Remote input failed',e);}
  }

  private async saveLayout(showMessage:boolean):Promise<void>{
    const envelope=this.settings();if(!envelope)return;
    try{
      await this.control.put('/api/settings',envelope.settings);await this.refreshSettings();
      window.dispatchEvent(new CustomEvent('twina-home-layout-changed'));
      if(showMessage)this.notice('Home layout restored','Default Home dashboard layout was restored.','success');
    }catch(e){this.fail('Home layout save failed',e);}
  }

  private resultFeedback(result:CommandResult,successTitle:string):void{this.notice(result.verified?successTitle:'Executed • state unverified',result.message,result.verified?'success':'warning');}
  private notice(title:string,detail:string,state:FeedbackState):void{this.feedback.set({title,detail,state});window.setTimeout(()=>this.feedback.set(null),3800);}
  private fail(title:string,error:unknown):void{const e=error as any;this.notice(title,e?.error?.message||e?.error?.detail||e?.message||'Unknown error','error');}
}
