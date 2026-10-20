import { Body, Controller, Get, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiTags } from '@nestjs/swagger';
import { FinishJobShiftRequest, finishJobShiftSchema, StartJobRequest, startJobSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser, UuidParam } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { JobsService } from './jobs.service';

@ApiTags('jobs')
@ApiBearerAuth()
@Controller('jobs')
export class JobsController {
  constructor(private readonly jobs: JobsService) {}

  @Get()
  @ApiOperation({ summary: "Job boards, today's paid shifts left, my Worker skill, an open shift and my last payslips" })
  overview(@CurrentUser() user: AuthUser) {
    return this.jobs.overview(user.userId);
  }

  @Post('start')
  @HttpCode(200)
  @ApiOperation({ summary: 'Start a part-time shift (or get back my open shift of the same job); returns its tasks' })
  @ApiZodBody(startJobSchema)
  start(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(startJobSchema)) dto: StartJobRequest) {
    return this.jobs.start(user.userId, dto.job);
  }

  @Post('shifts/:id/finish')
  @HttpCode(200)
  @ApiOperation({ summary: 'Hand in the answers: the server checks them, pays the wage and explains each task (repeat calls return the same payslip)' })
  @ApiZodBody(finishJobShiftSchema)
  finish(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(finishJobShiftSchema)) dto: FinishJobShiftRequest) {
    return this.jobs.finish(user.userId, id, dto.answers);
  }
}
