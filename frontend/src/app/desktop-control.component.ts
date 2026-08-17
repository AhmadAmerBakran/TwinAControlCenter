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

@Component({
  selector:'ta-desktop-control',
  standalone:true,
  imports:[CommonModule,FormsModule],
  templateUrl:'./desktop-control.component.html',
  styleUrl:'./desktop-control.component.css'
})
export class DesktopControlComponent implements OnInit,OnDestroy {
  @ViewChild('remoteImage') remoteImage?:ElementRef<HTMLImageElement>;

  readonly runtime=signal<DesktopRuntime|null>(null);
  readonly windows=signal<DesktopWindow[]>([]);
  readonly processes=signal<DesktopProcess[]>([]);
  readonly audioSessions=signal<AppAudioSession[]>([]);
  readonly settings=signal<SettingsEnvelope|null>(null);
  readonly feedback=signal<{state:FeedbackState;title:string;detail:string}|null>(null);
  readonly loading=signal(false);

  tab:DesktopTab='windows';
  windowFilter='';
  processFilter='';
  remoteFrameUrl='';
  remoteText='';
  private pollTimer?:number;
  private frameTimer?:number;
  private refreshing=false;
  private dragging=false;
  private lastMoveAt=0;

  constructor(private readonly control:ControlService){}

  async ngOnInit():Promise<void>{
    await this.refreshEverything();
    this.pollTimer=window.setInterval(()=>void this.pollVisibleState(),1600);
  }

  ngOnDestroy():void{
    if(this.pollTimer)window.clearInterval(this.pollTimer);
    this.stopFrames();
  }

  setTab(tab:DesktopTab):void{
    this.tab=tab;
    if(tab==='remote')this.startFrames();else this.stopFrames();
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
      await Promise.allSettled([this.refreshRuntime(),this.refreshWindows(),this.refreshProcesses(),this.refreshAudio(),this.refreshSettings()]);
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
      this.notice(enabled?'Remote control enabled':'Remote control disabled',enabled?'Screen interaction is enabled. Input commands remain reported as state-unverified because Windows cannot prove what an application did with each input event.':'Remote desktop remains view-only.','success');
    }catch(e){this.fail('Remote-control setting failed',e);await this.refreshSettings();}
  }

  remotePointerDown(event:PointerEvent):void{
    if(!this.remoteControlEnabled())return;
    event.preventDefault();this.dragging=true;(event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
    const p=this.pointerPosition(event);void this.sendInput({action:'leftdown',...p},false);
  }

  remotePointerMove(event:PointerEvent):void{
    if(!this.remoteControlEnabled()||!this.dragging)return;
    const now=performance.now();if(now-this.lastMoveAt<45)return;this.lastMoveAt=now;
    const p=this.pointerPosition(event);void this.sendInput({action:'move',...p},false);
  }

  remotePointerUp(event:PointerEvent):void{
    if(!this.remoteControlEnabled()||!this.dragging)return;
    event.preventDefault();this.dragging=false;
    const p=this.pointerPosition(event);void this.sendInput({action:'leftup',...p},false);
  }

  remoteContextMenu(event:MouseEvent):void{
    event.preventDefault();if(!this.remoteControlEnabled())return;
    const p=this.pointerPosition(event);void this.sendInput({action:'rightclick',...p},false);
  }

  remoteWheel(event:WheelEvent):void{
    if(!this.remoteControlEnabled())return;
    event.preventDefault();const p=this.pointerPosition(event);const delta=event.deltaY>0?-120:120;
    void this.sendInput({action:'wheel',delta,...p},false);
  }

  async remoteKey(key:string):Promise<void>{await this.sendInput({action:'key',key},true);}
  async sendRemoteText():Promise<void>{
    const text=this.remoteText;if(!text)return;
    await this.sendInput({action:'text',text},true);this.remoteText='';
  }

  homeCards():HomeCardPreference[]{return [...(this.settings()?.settings.ui.homeCards??[])].sort((a,b)=>a.order-b.order);}

  async toggleHomeCard(item:HomeCardPreference):Promise<void>{
    item.visible=!item.visible;await this.saveLayout(false);
  }

  async toggleCardSize(item:HomeCardPreference):Promise<void>{
    item.size=item.size==='wide'?'normal':'wide';await this.saveLayout(false);
  }

  async moveHomeCard(item:HomeCardPreference,direction:-1|1):Promise<void>{
    const envelope=this.settings();if(!envelope)return;
    const cards=[...envelope.settings.ui.homeCards].sort((a,b)=>a.order-b.order);
    const index=cards.findIndex(c=>c.key===item.key);const target=index+direction;
    if(index<0||target<0||target>=cards.length)return;
    [cards[index],cards[target]]=[cards[target],cards[index]];
    cards.forEach((card,i)=>card.order=(i+1)*10);
    envelope.settings.ui.homeCards=cards;
    await this.saveLayout(false);
  }

  async resetHomeLayout():Promise<void>{
    if(!confirm('Restore the default Home dashboard card order, visibility and sizes?'))return;
    const envelope=this.settings();if(!envelope)return;
    envelope.settings.ui.homeCards=[];
    await this.saveLayout(true);
  }

  formatBytes(n:number):string{
    if(!Number.isFinite(n))return'—';const u=['B','KB','MB','GB','TB'];let i=0;while(n>=1024&&i<u.length-1){n/=1024;i++;}return`${n<10&&i>0?n.toFixed(1):Math.round(n)} ${u[i]}`;
  }

  windowState(item:DesktopWindow):string{
    if(item.foreground)return'FOREGROUND';if(item.minimized)return'MINIMIZED';if(item.maximized)return'MAXIMIZED';return'OPEN';
  }

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

  private startFrames():void{
    this.stopFrames();this.refreshFrame();this.frameTimer=window.setInterval(()=>this.refreshFrame(),280);
  }

  private stopFrames():void{if(this.frameTimer)window.clearInterval(this.frameTimer);this.frameTimer=undefined;this.dragging=false;}
  private refreshFrame():void{this.remoteFrameUrl=`/api/desktop/frame?maxWidth=1600&quality=62&t=${Date.now()}`;}

  private pointerPosition(event:MouseEvent|PointerEvent|WheelEvent):{x:number;y:number}{
    const image=this.remoteImage?.nativeElement;const rect=image?.getBoundingClientRect();
    if(!rect||rect.width<=0||rect.height<=0)return{x:.5,y:.5};
    return{x:Math.max(0,Math.min(1,(event.clientX-rect.left)/rect.width)),y:Math.max(0,Math.min(1,(event.clientY-rect.top)/rect.height))};
  }

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
      await this.control.put('/api/settings',envelope.settings);
      await this.refreshSettings();
      window.dispatchEvent(new CustomEvent('twina-home-layout-changed'));
      if(showMessage)this.notice('Home layout restored','Default Home dashboard layout was restored.','success');
    }catch(e){this.fail('Home layout save failed',e);}
  }

  private resultFeedback(result:CommandResult,successTitle:string):void{
    this.notice(result.verified?successTitle:'Executed • state unverified',result.message,result.verified?'success':'warning');
  }

  private notice(title:string,detail:string,state:FeedbackState):void{
    this.feedback.set({title,detail,state});window.setTimeout(()=>this.feedback.set(null),3800);
  }

  private fail(title:string,error:unknown):void{
    const e=error as any;this.notice(title,e?.error?.message||e?.error?.detail||e?.message||'Unknown error','error');
  }
}
