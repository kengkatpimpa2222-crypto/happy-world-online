import { Global, Module } from '@nestjs/common';
import { SocialGraphService } from './social-graph.service';

@Global()
@Module({ providers: [SocialGraphService], exports: [SocialGraphService] })
export class SocialGraphModule {}
