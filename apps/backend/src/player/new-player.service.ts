import { Injectable } from '@nestjs/common';
import { AgeBand, User } from '@prisma/client';
import { BUSINESS_PRODUCTS, STARTER_ITEM_IDS, STARTING_GEMS, STARTING_GOLD } from '@hwo/shared-contracts';
import { randomInt } from 'node:crypto';
import { randomFriendCode } from '../common/crypto';
import { Tx } from '../prisma/prisma.service';
import { WalletService } from '../wallet/wallet.service';

export interface NewPlayerInput {
  ageBand: AgeBand;
  locale: string;
  deviceIdHash: string;
}

/** Everything a new player starts with: profile, wallets, starter items, home and a small shop. */
@Injectable()
export class NewPlayerService {
  constructor(private readonly wallet: WalletService) {}

  async create(tx: Tx, input: NewPlayerInput): Promise<User> {
    const user = await tx.user.create({
      data: { ageBand: input.ageBand, locale: input.locale, deviceIdHash: input.deviceIdHash },
    });

    await tx.playerProfile.create({
      data: {
        userId: user.id,
        displayName: `Player${randomInt(1000, 9999)}`,
        friendCode: randomFriendCode(),
        avatar: { bodyColor: '#F2C9A0', outfitItemId: 'outfit_basic_tee', hairItemId: 'hair_short_brown', accessoryItemId: null },
      },
    });

    await tx.playerWallet.createMany({
      data: [
        { userId: user.id, currency: 'GOLD' },
        { userId: user.id, currency: 'GEMS' },
      ],
    });
    if (STARTING_GOLD > 0) {
      await this.wallet.applyChange(tx, { userId: user.id, currency: 'GOLD', amount: STARTING_GOLD, type: 'SIGNUP_BONUS', idempotencyKey: 'signup:GOLD' });
    }
    if (STARTING_GEMS > 0) {
      await this.wallet.applyChange(tx, { userId: user.id, currency: 'GEMS', amount: STARTING_GEMS, type: 'SIGNUP_BONUS', idempotencyKey: 'signup:GEMS' });
    }

    await tx.playerInventory.createMany({
      data: STARTER_ITEM_IDS.map((itemId) => ({ userId: user.id, itemId, quantity: 1 })),
    });

    await tx.playerHome.create({ data: { userId: user.id } });

    await tx.business.create({
      data: {
        userId: user.id,
        name: 'My Lemonade Stand',
        inventory: { create: { productCode: 'LEMONADE', quantity: 0, sellPrice: BUSINESS_PRODUCTS.LEMONADE.basePrice } },
      },
    });

    return user;
  }
}
