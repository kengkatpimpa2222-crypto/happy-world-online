import { Module } from '@nestjs/common';
import { QuestsModule } from '../quests/quests.module';
import { BusinessController } from './business.controller';
import { BusinessService } from './business.service';

@Module({ imports: [QuestsModule], controllers: [BusinessController], providers: [BusinessService] })
export class BusinessModule {}
