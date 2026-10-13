import { Module } from '@nestjs/common';
import { QuestsModule } from '../quests/quests.module';
import { StoreController } from './store.controller';
import { StoreService } from './store.service';

@Module({ imports: [QuestsModule], controllers: [StoreController], providers: [StoreService] })
export class StoreModule {}
