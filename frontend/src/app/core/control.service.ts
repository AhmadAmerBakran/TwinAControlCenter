import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import * as signalR from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';

export type ConnectionState = 'online' | 'degraded' | 'offline';
export type ModuleState = 'ready' | 'busy' | 'offline' | 'warning';
export interface ObsAudioSourceState { name:string; muted:boolean; volumeDb:number; }
export interface ControlSnapshot {
  pc:ConnectionState; vpn:ConnectionState; agent:ConnectionState; obs:ModuleState;
  recording:boolean; recordingPaused:boolean; recordingSeconds:number; scene:string; replayBuffer:boolean; studioMode:boolean;
  scenes:string[]; obsAudioSources:ObsAudioSourceState[]; micMuted:boolean; desktopMuted:boolean;
  cpu:number; gpu:number; ram:number; gpuTemp:number; networkDownMbps:number; networkUpMbps:number;
  currentGame:string|null; sessionName:string; masterVolume:number; masterMuted:boolean; lastScreenshotPath:string|null;
}
export interface CommandResult { ok:boolean; verified:boolean; message:string; command:string; timestamp:string; data?:string|null; }
export interface AudioDevice { id:string; name:string; flow:'render'|'capture'; isDefault:boolean; }
export interface SoundInfo { id:string; name:string; fileName:string; extension:string; size:number; modifiedAt:string; }
export interface GameInfo { id:string; appId:number; name:string; installDirectory:string; libraryPath:string; launchUri:string; installed:boolean; running:boolean; runningProcess?:string|null; custom?:boolean; config?:any; }
export interface GameProfile { gameId:string; launchObs:boolean; launchDiscord:boolean; audioOutputDeviceId:string|null; obsScene:string|null; ensureReplayBuffer:boolean; ensureRecording:boolean; masterVolume:number|null; }
export interface DriveInfo { name:string; label:string; format:string; totalBytes:number; freeBytes:number; }
export interface FileEntry { name:string; fullPath:string; isDirectory:boolean; size:number; modifiedAt:string; extension:string; protected:boolean; }
export interface NetworkInfo { name:string; description:string; linkSpeed:string; downMbps:number; upMbps:number; }
export interface SystemDetails { network:NetworkInfo; uptime:string; drives:DriveInfo[]; machineName:string; osDescription:string; }
export interface DevProject { id:string; name:string; workingDirectory:string; solutionOrProject:string; buildCommand:string; testCommand:string; runCommand:string; idePath:string; }
export interface DevStatus { id:string; name:string; workingDirectory:string; exists:boolean; branch:string; changedFiles:number; gitSummary:string; dotnetVersion:string; nodeVersion:string; gitVersion:string; dockerVersion:string; }
export interface FlowStep { command:string; payload:Record<string,string>; delayAfterMs:number; continueOnError:boolean; }
export interface FlowConfig { id:string; name:string; category:string; steps:FlowStep[]; }
export interface MqttDevice { id:string; name:string; stateTopic:string; commandTopic:string; onPayload:string; offPayload:string; }
export interface MqttDeviceState { id:string; name:string; value:string|null; online:boolean; stateTopic:string; }

export interface DesktopRuntime {
  steamRunning:boolean;
  discordRunning:boolean;
  obsRunning:boolean;
  foregroundTitle:string;
  foregroundProcess:string;
  foregroundPid:number;
  windowCount:number;
  processCount:number;
}
export interface DesktopWindow {
  handle:number;
  title:string;
  processName:string;
  pid:number;
  foreground:boolean;
  minimized:boolean;
  maximized:boolean;
}
export interface DesktopProcess {
  pid:number;
  name:string;
  windowTitle:string;
  memoryBytes:number;
  hasWindow:boolean;
  responding:boolean;
  protected:boolean;
}
export interface AppAudioSession {
  pid:number;
  processName:string;
  displayName:string;
  volume:number;
  muted:boolean;
  sessionCount:number;
}
export interface HomeCardPreference {
  key:string;
  label:string;
  visible:boolean;
  size:'normal'|'wide'|string;
  order:number;
}

export interface AppSettings {
  customGames:any[];
  gameProfiles:GameProfile[];
  devProjects:DevProject[];
  flows:FlowConfig[];
  mqtt:{enabled:boolean;host:string;port:number;tls:boolean;username:string;devices:MqttDevice[]};
  ui:{confirmPowerActions:boolean;protectSystemPaths:boolean;enableRemoteControl:boolean;keepScreenAwake:boolean;homeCards:HomeCardPreference[]};
}
export interface SettingsEnvelope { version:string; settings:AppSettings; configPath:string; obsPasswordStored:boolean; mqttPasswordStored:boolean; }

const offline:ControlSnapshot={pc:'offline',vpn:'offline',agent:'offline',obs:'offline',recording:false,recordingPaused:false,recordingSeconds:0,scene:'—',replayBuffer:false,studioMode:false,scenes:[],obsAudioSources:[],micMuted:false,desktopMuted:false,cpu:0,gpu:0,ram:0,gpuTemp:0,networkDownMbps:0,networkUpMbps:0,currentGame:null,sessionName:'TWIN A Control Center',masterVolume:0,masterMuted:false,lastScreenshotPath:null};

@Injectable({providedIn:'root'})
export class ControlService {
  readonly snapshot=signal<ControlSnapshot>(offline);
  readonly connected=signal(false);
  private hub?:signalR.HubConnection;
  constructor(private readonly http:HttpClient){}

  async connect():Promise<void>{
    this.hub=new signalR.HubConnectionBuilder().withUrl('/hubs/state').withAutomaticReconnect([0,1000,3000,8000,15000]).configureLogging(signalR.LogLevel.Warning).build();
    this.hub.on('snapshot',(state:ControlSnapshot)=>this.snapshot.set(state));
    this.hub.onreconnected(()=>this.connected.set(true)); this.hub.onclose(()=>this.connected.set(false));
    try{await this.hub.start();this.connected.set(true);const initial=await firstValueFrom(this.http.get<ControlSnapshot>('/api/state'));if(initial)this.snapshot.set(initial);}catch{this.connected.set(false);this.snapshot.set(offline);}
  }

  async execute(command:string,payload:Record<string,unknown>={}):Promise<CommandResult>{
    if(!this.connected())return{ok:false,verified:false,message:'Control Server is offline. The command was not sent.',command,timestamp:new Date().toISOString()};
    try{return await firstValueFrom(this.http.post<CommandResult>(`/api/commands/${encodeURIComponent(command)}`,payload));}
    catch(error){if(error instanceof HttpErrorResponse){const body=error.error as Partial<CommandResult>|null;return{ok:false,verified:false,message:body?.message||error.message||'Command failed.',command,timestamp:body?.timestamp||new Date().toISOString(),data:body?.data??null};}return{ok:false,verified:false,message:'Command failed before a verified response was received.',command,timestamp:new Date().toISOString()};}
  }

  get<T>(url:string):Promise<T>{return firstValueFrom(this.http.get<T>(url));}
  post<T>(url:string,body:unknown):Promise<T>{return firstValueFrom(this.http.post<T>(url,body));}
  put<T>(url:string,body:unknown):Promise<T>{return firstValueFrom(this.http.put<T>(url,body));}
  delete<T>(url:string):Promise<T>{return firstValueFrom(this.http.delete<T>(url));}
  uploadRaw<T>(url:string,file:File):Promise<T>{return firstValueFrom(this.http.post<T>(url,file,{headers:{'Content-Type':'application/octet-stream'}}));}
  downloadUrl(path:string):string{return `/api/files/download?path=${encodeURIComponent(path)}`;}
}
