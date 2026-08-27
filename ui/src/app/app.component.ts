import { Component, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { IpcService } from './ipc/ipc.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  hostStatus = 'Checking host…';

  constructor(private readonly ipc: IpcService) {}

  ngOnInit(): void {
    this.ipc.request('ping').then((reply) => {
      this.hostStatus = reply.ok ? 'Host connected' : (reply.error ?? 'Host unavailable');
    });
  }
}
