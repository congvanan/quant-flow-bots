import 'package:flutter/material.dart';

import '../../core/format.dart';
import '../../core/theme/app_theme.dart';

/// Bộ widget UI dùng lại toàn app — đồng nhất phong cách, gọn màn hình.

/// Thẻ section có tiêu đề + action tùy chọn.
class SectionCard extends StatelessWidget {
  final String title;
  final Widget child;
  final Widget? trailing;
  final EdgeInsets padding;
  const SectionCard({
    super.key,
    required this.title,
    required this.child,
    this.trailing,
    this.padding = const EdgeInsets.all(14),
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: padding,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(children: [
              Expanded(child: Text(title, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14))),
              ?trailing,
            ]),
            const SizedBox(height: 10),
            child,
          ],
        ),
      ),
    );
  }
}

/// Ô số liệu nhỏ (label trên, value to dưới), màu tùy chọn.
class StatTile extends StatelessWidget {
  final String label;
  final String value;
  final Color? valueColor;
  const StatTile({super.key, required this.label, required this.value, this.valueColor});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppTheme.bg,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: AppTheme.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(label, style: const TextStyle(fontSize: 11, color: Colors.grey), overflow: TextOverflow.ellipsis),
          const SizedBox(height: 4),
          Text(value, style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: valueColor)),
        ],
      ),
    );
  }
}

/// Text PnL có dấu + màu xanh/đỏ.
class PnlText extends StatelessWidget {
  final num value;
  final bool percent;
  final double size;
  final bool withSign;
  const PnlText(this.value, {super.key, this.percent = false, this.size = 13, this.withSign = true});

  @override
  Widget build(BuildContext context) {
    final txt = percent ? Fmt.pct(value, sign: withSign) : (withSign ? Fmt.signedUsd(value) : Fmt.usd(value));
    return Text(txt, style: TextStyle(color: Fmt.pnlColor(value), fontSize: size, fontWeight: FontWeight.w600));
  }
}

class AppChip extends StatelessWidget {
  final String label;
  final Color? color;
  final bool filled;
  const AppChip(this.label, {super.key, this.color, this.filled = false});

  @override
  Widget build(BuildContext context) {
    final c = color ?? Colors.grey;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: filled ? c.withValues(alpha: 0.15) : AppTheme.bg,
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: filled ? c.withValues(alpha: 0.5) : AppTheme.border),
      ),
      child: Text(label,
          style: TextStyle(fontSize: 11, color: filled ? c : Colors.grey[300], fontWeight: FontWeight.w500)),
    );
  }
}

class LiveBadge extends StatelessWidget {
  final bool connected;
  const LiveBadge({super.key, required this.connected});
  @override
  Widget build(BuildContext context) {
    final c = connected ? AppTheme.up : Colors.grey;
    return Row(mainAxisSize: MainAxisSize.min, children: [
      Icon(Icons.circle, size: 9, color: c),
      const SizedBox(width: 4),
      Text(connected ? 'LIVE' : 'offline', style: TextStyle(fontSize: 11, color: c, fontWeight: FontWeight.w600)),
    ]);
  }
}

class StatusPill extends StatelessWidget {
  final String text;
  final Color color;
  const StatusPill(this.text, this.color, {super.key});
  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.15),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: color.withValues(alpha: 0.5)),
        ),
        child: Text(text, style: TextStyle(color: color, fontSize: 11, fontWeight: FontWeight.w600)),
      );
}

class EmptyState extends StatelessWidget {
  final String message;
  final IconData icon;
  const EmptyState(this.message, {super.key, this.icon = Icons.inbox_outlined});
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 40),
        child: Center(
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(icon, size: 40, color: Colors.grey[700]),
            const SizedBox(height: 10),
            Text(message, style: const TextStyle(color: Colors.grey)),
          ]),
        ),
      );
}

class ErrorRetry extends StatelessWidget {
  final Object error;
  final VoidCallback onRetry;
  const ErrorRetry(this.error, this.onRetry, {super.key});
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.all(24),
        child: Center(
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.cloud_off, size: 36, color: AppTheme.down),
            const SizedBox(height: 10),
            Text('$error', textAlign: TextAlign.center, style: const TextStyle(color: AppTheme.down)),
            const SizedBox(height: 12),
            FilledButton(onPressed: onRetry, child: const Text('Thử lại')),
          ]),
        ),
      );
}

class LoadingBlock extends StatelessWidget {
  final double height;
  const LoadingBlock({super.key, this.height = 80});
  @override
  Widget build(BuildContext context) =>
      SizedBox(height: height, child: const Center(child: CircularProgressIndicator(strokeWidth: 2)));
}

/// Hàng "label — value" gọn cho list chi tiết.
class KvRow extends StatelessWidget {
  final String k;
  final String v;
  final Color? valueColor;
  const KvRow(this.k, this.v, {super.key, this.valueColor});
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          Text(k, style: const TextStyle(fontSize: 12.5, color: Colors.grey)),
          Text(v, style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600, color: valueColor)),
        ]),
      );
}

