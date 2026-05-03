package com.example.app.chats

import android.graphics.Bitmap
import android.graphics.Color
import androidx.core.content.ContextCompat
import android.media.MediaMetadataRetriever
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import coil.request.CachePolicy
import coil.request.videoFramePercent
import coil.request.videoFrameMillis
import com.example.app.R
import com.example.app.api.MessageItem
import java.util.Locale
import kotlin.concurrent.thread
import org.json.JSONObject
import org.json.JSONException

class MessagesAdapter(
    private val selfAliases: Set<String>,
    private val incomingAvatarUrl: String?,
    private val incomingAvatarFallback: String,
    private val onClick: (item: MessageItem) -> Unit,
    private val onMediaClick: (item: MessageItem, mediaUrl: String) -> Unit,
    private val onLongPress: (item: MessageItem) -> Unit,
    private val onActionClick: (item: MessageItem, action: String) -> Unit,
    private val onReplyJump: (replyToId: Int) -> Unit,
    private val isSelected: (messageId: Int) -> Boolean,
    private val isHighlighted: (messageId: Int) -> Boolean
) : ListAdapter<MessageItem, MessagesAdapter.VH>(Diff()) {
    companion object {
        private const val VT_SYSTEM = 0
        private const val VT_IN = 1
        private const val VT_OUT = 2
    }

    override fun getItemViewType(position: Int): Int {
        val item = getItem(position)
        val st = item.senderType.lowercase(Locale.getDefault())
        if (st == "system") return VT_SYSTEM

        val sender = item.senderId?.trim().orEmpty()
        if (st == "user" && sender.isNotEmpty() && selfAliases.contains(sender.lowercase(Locale.getDefault()))) {
            return VT_OUT
        }
        return VT_IN
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val inflater = LayoutInflater.from(parent.context)
        val layout = when (viewType) {
            VT_OUT -> R.layout.item_message_out
            VT_IN -> R.layout.item_message_in
            else -> R.layout.item_message_system
        }
        val v = inflater.inflate(layout, parent, false)
        return VH(
            v,
            incomingAvatarUrl,
            incomingAvatarFallback,
            onClick,
            onMediaClick,
            onLongPress,
            onActionClick,
            onReplyJump,
            isSelected,
            isHighlighted,
            viewType
        )
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        holder.bind(getItem(position), position, currentList)
    }

    class VH(
        itemView: View,
        private val incomingAvatarUrl: String?,
        private val incomingAvatarFallback: String,
        private val onClick: (MessageItem) -> Unit,
        private val onMediaClick: (MessageItem, String) -> Unit,
        private val onLongPress: (MessageItem) -> Unit,
        private val onActionClick: (MessageItem, String) -> Unit,
        private val onReplyJump: (Int) -> Unit,
        private val isSelected: (Int) -> Boolean,
        private val isHighlighted: (Int) -> Boolean,
        private val viewType: Int
    ) : RecyclerView.ViewHolder(itemView) {
        private val tvText: TextView = itemView.findViewById(R.id.tvMessageText)
        private val tvTime: TextView? = itemView.findViewById(R.id.tvMessageTime)
        private val tvStatus: TextView? = itemView.findViewById(R.id.tvMessageStatus)
        private val bubble: View? = itemView.findViewById(R.id.messageBubble)
        private val replyContainer: View? = itemView.findViewById(R.id.replyContainer)
        private val tvReplyLabel: TextView? = itemView.findViewById(R.id.tvReplyLabel)
        private val tvReplyText: TextView? = itemView.findViewById(R.id.tvReplyText)
        private val mediaWrap: View? = itemView.findViewById(R.id.messageMediaWrap)
        private val ivMedia: ImageView? = itemView.findViewById(R.id.ivMessageMedia)
        private val tvVideoPlay: TextView? = itemView.findViewById(R.id.tvMessageVideoPlay)
        private val tvMediaMore: TextView? = itemView.findViewById(R.id.tvMessageMediaMore)
        private val btnMessageAction: TextView? = itemView.findViewById(R.id.btnMessageAction)
        private val coinTransferCard: View? = itemView.findViewById(R.id.coinTransferCard)
        private val tvCoinTransferTitle: TextView? = itemView.findViewById(R.id.tvCoinTransferTitle)
        private val tvCoinTransferAmount: TextView? = itemView.findViewById(R.id.tvCoinTransferAmount)
        private val tvCoinTransferSubtitle: TextView? = itemView.findViewById(R.id.tvCoinTransferSubtitle)
        private val tvCoinTransferComment: TextView? = itemView.findViewById(R.id.tvCoinTransferComment)
        private val tvCoinTransferMetaTime: TextView? = itemView.findViewById(R.id.tvCoinTransferMetaTime)
        private val tvCoinTransferMetaStatus: TextView? = itemView.findViewById(R.id.tvCoinTransferMetaStatus)
        private val incomingAvatar: View? = itemView.findViewById(R.id.incomingAvatar)
        private val incomingAvatarImage: ImageView? = itemView.findViewById(R.id.ivIncomingAvatar)
        private val incomingAvatarText: TextView? = itemView.findViewById(R.id.tvIncomingAvatarText)

        fun bind(item: MessageItem, position: Int, all: List<MessageItem>) {
            val media = parseChatMedia(item.metaJson)
            val visualMedia = media.filter {
                it.second.equals("image", ignoreCase = true) || it.second.equals("video", ignoreCase = true)
            }
            if (visualMedia.isNotEmpty() && ivMedia != null) {
                mediaWrap?.visibility = View.VISIBLE
                val (url, kind) = visualMedia.first()
                val isVideo = kind.equals("video", ignoreCase = true)
                tvVideoPlay?.visibility = if (isVideo) View.VISIBLE else View.GONE
                val extra = visualMedia.size - 1
                tvMediaMore?.visibility = if (extra > 0) View.VISIBLE else View.GONE
                tvMediaMore?.text = if (extra > 0) "+$extra" else ""
                if (isVideo) {
                    bindVideoPreview(url)
                } else {
                    ivMedia.load(url) {
                        crossfade(true)
                        error(R.drawable.ic_launcher_simple)
                        memoryCachePolicy(CachePolicy.ENABLED)
                        diskCachePolicy(CachePolicy.ENABLED)
                    }
                }
                val openMedia = View.OnClickListener { onMediaClick(item, url) }
                mediaWrap?.setOnClickListener(openMedia)
                ivMedia.setOnClickListener(openMedia)
                mediaWrap?.setOnLongClickListener { onLongPress(item); true }
                ivMedia.setOnLongClickListener { onLongPress(item); true }
            } else {
                mediaWrap?.visibility = View.GONE
                ivMedia?.setImageDrawable(null)
                tvVideoPlay?.visibility = View.GONE
                tvMediaMore?.visibility = View.GONE
                mediaWrap?.setOnClickListener(null)
                ivMedia?.setOnClickListener(null)
                mediaWrap?.setOnLongClickListener(null)
                ivMedia?.setOnLongClickListener(null)
            }

            val text = item.text.trim()
            val firstNonVisualKind = media.firstOrNull()?.second?.trim()?.lowercase(Locale.getDefault()).orEmpty()
            val fallbackNonVisualText = if (text.isEmpty() && firstNonVisualKind == "apk") "APK файл" else item.text
            tvText.text = fallbackNonVisualText
            tvText.visibility = if (text.isEmpty() && visualMedia.isNotEmpty()) View.GONE else View.VISIBLE
            val isCoinTransfer = bindCoinTransfer(item)
            val baseTime = ChatTimeFormat.format(item.createdAtUtc)
            tvTime?.text = if (item.isEdited) "$baseTime · редакт." else baseTime
            bindStatus(item)
            bindReply(item.metaJson)
            bindMessageAction(item)

            // Telegram-like: tap anywhere opens menu / selection handler
            itemView.setOnClickListener { onClick(item) }
            bubble?.setOnClickListener { onClick(item) }
            tvText.setOnClickListener { onClick(item) }

            // Long press anywhere opens menu
            itemView.setOnLongClickListener { onLongPress(item); true }
            bubble?.setOnLongClickListener { onLongPress(item); true }
            tvText.setOnLongClickListener { onLongPress(item); true }

            val selected = isSelected(item.id)
            val highlighted = isHighlighted(item.id)
            val prev = all.getOrNull(position - 1)
            val next = all.getOrNull(position + 1)
            val prevSame = prev?.let { isSameBubbleGroup(item, it) } == true
            val nextSame = next?.let { isSameBubbleGroup(item, it) } == true
            applyGroupSpacing(prevSame, nextSame)
            if (isCoinTransfer) {
                tvTime?.visibility = View.GONE
                tvStatus?.visibility = View.GONE
            } else {
                applyMetaVisibility(nextSame)
            }
            if (viewType == VT_IN) {
                val showAvatar = !prevSame
                incomingAvatar?.visibility = if (showAvatar) View.VISIBLE else View.INVISIBLE
                if (showAvatar) bindIncomingAvatar(item)
            }
            if (isCoinTransfer) {
                bubble?.background = null
            } else {
                bubble?.setBackgroundResource(resolveBubbleBackground(prevSame, nextSame))
            }
            val bg = when {
                highlighted -> Color.parseColor("#334A90E2")
                selected -> ContextCompat.getColor(itemView.context, R.color.thread_item_selected_bg)
                else -> Color.TRANSPARENT
            }
            itemView.setBackgroundColor(bg)
        }

        private fun applyGroupSpacing(prevSame: Boolean, nextSame: Boolean) {
            val density = itemView.resources.displayMetrics.density
            val top = if (prevSame) 2 else 8
            val bottom = if (nextSame) 2 else 8
            (itemView.layoutParams as? RecyclerView.LayoutParams)?.let { lp ->
                lp.topMargin = (top * density).toInt()
                lp.bottomMargin = (bottom * density).toInt()
                itemView.layoutParams = lp
            }
        }

        private fun resolveBubbleBackground(prevSame: Boolean, nextSame: Boolean): Int {
            return when (viewType) {
                VT_OUT -> when {
                    !prevSame && !nextSame -> R.drawable.bg_message_out_single
                    !prevSame && nextSame -> R.drawable.bg_message_out_top
                    prevSame && nextSame -> R.drawable.bg_message_out_middle
                    else -> R.drawable.bg_message_out_bottom
                }
                else -> when {
                    !prevSame && !nextSame -> R.drawable.bg_message_in_single
                    !prevSame && nextSame -> R.drawable.bg_message_in_top
                    prevSame && nextSame -> R.drawable.bg_message_in_middle
                    else -> R.drawable.bg_message_in_bottom
                }
            }
        }

        private fun isSameBubbleGroup(current: MessageItem, other: MessageItem): Boolean {
            if (current.senderType.equals("system", ignoreCase = true)) return false
            if (other.senderType.equals("system", ignoreCase = true)) return false
            if (!current.senderType.equals(other.senderType, ignoreCase = true)) return false
            val currentSender = current.senderId?.trim().orEmpty().lowercase(Locale.getDefault())
            val otherSender = other.senderId?.trim().orEmpty().lowercase(Locale.getDefault())
            if (currentSender.isNotBlank() && otherSender.isNotBlank()) {
                return currentSender == otherSender
            }
            return current.senderName?.trim().orEmpty()
                .equals(other.senderName?.trim().orEmpty(), ignoreCase = true)
        }

        private fun bindStatus(item: MessageItem) {
            if (viewType != VT_OUT) {
                tvStatus?.visibility = View.GONE
                return
            }
            tvStatus?.visibility = View.VISIBLE
            tvStatus?.text = if (item.isRead) "✓✓" else "✓"
            tvStatus?.setTextColor(
                ContextCompat.getColor(
                    itemView.context,
                    if (item.isRead) R.color.thread_outgoing_check else R.color.thread_outgoing_check_unread
                )
            )
        }

        private fun bindIncomingAvatar(item: MessageItem) {
            val bySender = item.senderName
                ?.split(" ")
                ?.filter { it.isNotBlank() }
                ?.take(2)
                ?.joinToString("") { it.first().uppercase() }
                .orEmpty()
            val fallback = bySender.ifBlank { incomingAvatarFallback.ifBlank { "Ч" } }
            incomingAvatarText?.text = fallback
            val url = incomingAvatarUrl?.trim().orEmpty()
            if (url.isNotBlank()) {
                incomingAvatarImage?.visibility = View.VISIBLE
                incomingAvatarText?.visibility = View.GONE
                incomingAvatarImage?.load(url) {
                    crossfade(true)
                    error(R.drawable.ic_launcher_simple)
                }
            } else {
                incomingAvatarImage?.setImageDrawable(null)
                incomingAvatarImage?.visibility = View.GONE
                incomingAvatarText?.visibility = View.VISIBLE
            }
        }

        private fun applyMetaVisibility(nextSame: Boolean) {
            val showMeta = !nextSame
            setMetaVisibleWithFade(tvTime, showMeta)
            if (viewType == VT_OUT) setMetaVisibleWithFade(tvStatus, showMeta)
        }

        private fun setMetaVisibleWithFade(view: TextView?, visible: Boolean) {
            view ?: return
            if (!visible) {
                view.animate().cancel()
                view.alpha = 1f
                view.visibility = View.GONE
                return
            }
            if (view.visibility == View.VISIBLE && view.alpha >= 0.99f) return
            view.animate().cancel()
            view.alpha = 0f
            view.visibility = View.VISIBLE
            view.animate()
                .alpha(1f)
                .setDuration(130L)
                .start()
        }

        private fun bindReply(metaJson: String?) {
            val meta = metaJson?.trim().orEmpty()
            if (meta.isBlank()) {
                replyContainer?.visibility = View.GONE
                return
            }
            val (replyToId, replyText, replySender) = runCatching {
                val obj = JSONObject(meta)
                val id = obj.optInt("replyToId", 0)
                val txt = obj.optString("replyText", "").trim()
                val sender = obj.optString("replySender", "").trim()
                Triple(id, txt, sender)
            }.getOrNull() ?: Triple(0, "", "")

            if (replyText.isBlank()) {
                replyContainer?.visibility = View.GONE
                return
            }
            tvReplyLabel?.text = if (replySender.isBlank()) "Ответ" else "Ответ $replySender"
            tvReplyText?.text = replyText
            replyContainer?.visibility = View.VISIBLE
            replyContainer?.setOnClickListener {
                if (replyToId > 0) onReplyJump(replyToId)
            }
        }

        private fun bindMessageAction(item: MessageItem) {
            val meta = item.metaJson?.trim().orEmpty()
            if (meta.isBlank()) {
                btnMessageAction?.visibility = View.GONE
                btnMessageAction?.setOnClickListener(null)
                return
            }
            val apkUrl = parseChatMedia(item.metaJson)
                .firstOrNull { it.second.equals("apk", ignoreCase = true) }
                ?.first
            val action = runCatching {
                val obj = JSONObject(meta)
                obj.optString("action", "").trim()
            }.getOrDefault("")
            val label = runCatching {
                val obj = JSONObject(meta)
                obj.optString("actionLabel", "").trim()
            }.getOrDefault("")
            if (action.isNotBlank() && label.isNotBlank()) {
                btnMessageAction?.text = label
                btnMessageAction?.visibility = View.VISIBLE
                btnMessageAction?.setOnClickListener { onActionClick(item, action) }
                btnMessageAction?.setOnLongClickListener { onLongPress(item); true }
                return
            }
            if (!apkUrl.isNullOrBlank()) {
                btnMessageAction?.text = "Скачать APK"
                btnMessageAction?.visibility = View.VISIBLE
                btnMessageAction?.setOnClickListener { onActionClick(item, "open_apk:$apkUrl") }
                btnMessageAction?.setOnLongClickListener { onLongPress(item); true }
                return
            }
            btnMessageAction?.visibility = View.GONE
            btnMessageAction?.setOnClickListener(null)
        }

        private fun bindCoinTransfer(item: MessageItem): Boolean {
            val meta = item.metaJson?.trim().orEmpty()
            if (meta.isBlank()) {
                coinTransferCard?.visibility = View.GONE
                tvTime?.visibility = View.VISIBLE
                tvStatus?.visibility = if (viewType == VT_OUT) View.VISIBLE else View.GONE
                return false
            }
            val parsed = runCatching {
                val obj = JSONObject(meta)
                if (!obj.optString("type", "").equals("coin_transfer", ignoreCase = true)) return@runCatching null
                val amount = obj.optInt("amount", 0)
                val fromName = obj.optString("fromName", "").trim()
                val fromLogin = obj.optString("fromLogin", "").trim()
                val toName = obj.optString("toName", "").trim()
                val toLogin = obj.optString("toLogin", "").trim()
                val comment = obj.optString("comment", "").trim()
                CoinTransferViewModel(
                    amount = amount,
                    fromLabel = fromName.ifBlank { fromLogin.ifBlank { "Отправитель" } },
                    toLabel = toName.ifBlank { toLogin.ifBlank { "Получатель" } },
                    comment = comment
                )
            }.getOrNull() ?: run {
                coinTransferCard?.visibility = View.GONE
                tvTime?.visibility = View.VISIBLE
                tvStatus?.visibility = if (viewType == VT_OUT) View.VISIBLE else View.GONE
                return false
            }

            tvText.visibility = View.GONE
            coinTransferCard?.visibility = View.VISIBLE
            tvCoinTransferTitle?.text = "Передача монет"
            tvCoinTransferAmount?.text = parsed.amount.toString()
            tvCoinTransferSubtitle?.text = "${parsed.fromLabel} → ${parsed.toLabel}"
            if (parsed.comment.isBlank()) {
                tvCoinTransferComment?.visibility = View.GONE
            } else {
                tvCoinTransferComment?.visibility = View.VISIBLE
                tvCoinTransferComment?.text = parsed.comment
            }
            val timeText = ChatTimeFormat.format(item.createdAtUtc)
            tvCoinTransferMetaTime?.text = timeText
            if (viewType == VT_OUT) {
                tvCoinTransferMetaStatus?.visibility = View.VISIBLE
                tvCoinTransferMetaStatus?.text = if (item.isRead) "✓✓" else "✓"
            } else {
                tvCoinTransferMetaStatus?.visibility = View.GONE
            }
            tvTime?.visibility = View.GONE
            tvStatus?.visibility = View.GONE
            return true
        }

        private fun parseChatMedia(metaJson: String?): List<Pair<String, String>> {
            val meta = metaJson?.trim().orEmpty()
            if (meta.isBlank()) return emptyList()
            return runCatching {
                val o = JSONObject(meta)
                val out = mutableListOf<Pair<String, String>>()
                val arr = o.optJSONArray("media")
                if (arr != null) {
                    for (i in 0 until arr.length()) {
                        val e = arr.optJSONObject(i) ?: continue
                        val url = e.optString("url", "").trim()
                        if (url.isBlank()) continue
                        val kind = e.optString("kind", "image").trim().ifBlank { "image" }
                        out += url to kind
                    }
                }
                if (out.isEmpty()) {
                    val url = o.optString("mediaUrl", "").trim()
                    if (url.isNotBlank()) {
                        val kind = o.optString("mediaKind", "image").trim().ifBlank { "image" }
                        out += url to kind
                    }
                }
                out
            }.getOrDefault(emptyList())
        }

        private fun bindVideoPreview(url: String) {
            val view = ivMedia ?: return
            view.tag = url
            view.setImageResource(R.drawable.ic_launcher_simple)

            thread(start = true) {
                val bmp = extractVideoFrame(url)
                view.post {
                    if (view.tag != url) return@post
                    if (bmp != null) {
                        view.setImageBitmap(bmp)
                    } else {
                        // Final fallback to Coil decoder if retriever failed.
                        view.load(url) {
                            crossfade(false)
                            error(R.drawable.ic_launcher_simple)
                            videoFramePercent(0.6)
                            videoFrameMillis(4000L)
                            allowHardware(false)
                        }
                    }
                }
            }
        }

        private fun extractVideoFrame(url: String): Bitmap? {
            val mmr = MediaMetadataRetriever()
            return try {
                mmr.setDataSource(url, HashMap())
                val durMs = mmr.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)
                    ?.toLongOrNull()
                    ?.coerceAtLeast(0L)
                    ?: 0L
                val atUs = if (durMs > 0L) durMs * 1000L * 3L / 5L else 4_000_000L
                mmr.getFrameAtTime(atUs, MediaMetadataRetriever.OPTION_CLOSEST_SYNC)
                    ?: mmr.getFrameAtTime(4_000_000L, MediaMetadataRetriever.OPTION_CLOSEST)
            } catch (_: Exception) {
                null
            } finally {
                runCatching { mmr.release() }
            }
        }

        private data class CoinTransferViewModel(
            val amount: Int,
            val fromLabel: String,
            val toLabel: String,
            val comment: String
        )
    }

    private class Diff : DiffUtil.ItemCallback<MessageItem>() {
        override fun areItemsTheSame(oldItem: MessageItem, newItem: MessageItem) = oldItem.id == newItem.id
        override fun areContentsTheSame(oldItem: MessageItem, newItem: MessageItem) = oldItem == newItem
    }

    fun submit(items: List<MessageItem>) {
        submitList(items)
    }
}

