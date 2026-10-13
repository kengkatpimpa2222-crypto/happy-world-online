import { Injectable } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import { CreateReportRequest, ErrorCode } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuditService } from '../audit/audit.service';
import { PrismaService } from '../prisma/prisma.service';
import { BlocksService } from './blocks.service';

@Injectable()
export class ModerationService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly blocks: BlocksService,
    private readonly audit: AuditService,
  ) {}

  /** Files a report. The same open report (reporter, player, reason) is returned instead of duplicated. */
  async report(userId: string, dto: CreateReportRequest) {
    if (dto.reportedPlayerId === userId) throw AppException.badRequest(ErrorCode.CANNOT_TARGET_SELF, 'You cannot report yourself');
    const target = await this.prisma.user.findUnique({ where: { id: dto.reportedPlayerId }, select: { id: true } });
    if (!target) throw AppException.notFound('Player not found');

    const where = { reporterId: userId, reportedId: dto.reportedPlayerId, reason: dto.reason, status: 'OPEN' as const };
    let report = await this.prisma.moderationReport.findFirst({ where });
    let created = false;
    if (!report) {
      try {
        report = await this.prisma.moderationReport.create({
          data: { reporterId: userId, reportedId: dto.reportedPlayerId, reason: dto.reason, contextCode: dto.contextCode },
        });
        created = true;
        await this.audit.log({ actorUserId: userId, action: 'moderation.report_created', targetType: 'user', targetId: dto.reportedPlayerId, metadata: { reportId: report.id, reason: dto.reason } });
      } catch (e) {
        // parallel retry hit the partial unique index: return the report that won
        if (!(e instanceof Prisma.PrismaClientKnownRequestError && e.code === 'P2002')) throw e;
        report = await this.prisma.moderationReport.findFirstOrThrow({ where });
      }
    }
    if (dto.alsoBlock) await this.blocks.block(userId, dto.reportedPlayerId);
    return { reportId: report.id, status: report.status, created, blocked: dto.alsoBlock };
  }

  /** The reporter sees status only, never the moderator's notes or actions on the other player. */
  async mine(userId: string) {
    const rows = await this.prisma.moderationReport.findMany({
      where: { reporterId: userId },
      include: { reported: { select: { profile: { select: { displayName: true } } } } },
      orderBy: { createdAt: 'desc' },
      take: 50,
    });
    return rows.map((r) => ({
      reportId: r.id,
      reportedPlayerId: r.reportedId,
      reportedDisplayName: r.reported.profile?.displayName ?? null,
      reason: r.reason,
      contextCode: r.contextCode,
      status: r.status,
      createdAt: r.createdAt,
    }));
  }
}
