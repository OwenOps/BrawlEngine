import { Component, OnInit } from '@angular/core';
import { IpcService } from '../ipc/ipc.service';

export interface CatalogItem {
  id: number;
  name: string;
  author: string;
  thumbnailUrl: string | null;
  category: string;
  profileUrl: string;
}

export interface CatalogPage {
  items: CatalogItem[];
  nextApiPage: number;
  complete: boolean;
}

@Component({
  selector: 'app-maps-page',
  templateUrl: './maps.page.html',
  styleUrl: './maps.page.scss',
})
export class MapsPage implements OnInit {
  items: CatalogItem[] = [];
  loading = false;
  error: string | null = null;
  complete = false;
  private nextApiPage = 1;

  constructor(private readonly ipc: IpcService) {}

  ngOnInit(): void {
    this.load(true);
  }

  loadMore(): void {
    this.load(false);
  }

  private load(reset: boolean): void {
    if (this.loading) {
      return;
    }
    this.loading = true;
    this.error = null;
    const page = reset ? 1 : this.nextApiPage;
    this.ipc.request('catalog.maps', { page }).then((reply) => {
      this.loading = false;
      if (!reply.ok) {
        this.error = reply.error ?? 'Could not load maps.';
        return;
      }
      const data = reply.payload as CatalogPage | undefined;
      if (!data) {
        this.error = 'Empty catalog response.';
        return;
      }
      this.items = reset ? data.items : [...this.items, ...data.items];
      this.nextApiPage = data.nextApiPage;
      this.complete = data.complete;
    });
  }
}
