package com.example.app.chats

import android.graphics.Bitmap
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

class MessagesAdapter(
    private val selfLogin: String,
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
        if (st == "user" && sender.isNotEmpty() && sender.equals(selfLogin, ignoreCase = true)) {
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
        return VH(v, onClick, onMediaClick, onLongPress, onActionClick, onReplyJump, isSelected, isHighlighted, viewType)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        holder.bind(getItem(position), position, currentList)
    }

    class VH(
        itemView: View,
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

        fun bind(item: MessageItem, position: Int, all: List<MessageItem>) {
            val media = parseChatMedia(item.metaJson)
            if (media.isNotEmpty() && ivMedia != null) {
                mediaWrap?.visibility = View.VISIBLE
                val (url, kind) = media.first()
                val isVideo = kind.equals("video", ignoreCase = true)
                tvVideoPlay?.visibility = if (isVideo) View.VISIBLE else View.GONE
                val extra = media.size - 1
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
            tvText.text = item.text
            tvText.visibility = if (text.isEmpty() && media.isNotEmpty()) View.GONE else View.VISIBLE
            tvTime?.text = ChatTimeFormat.format(item.createdAtUtc)
            bindStatus(item, position, all)
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

            val selected = isSelected(item.id) || isHighlighted(item.id)
            bubble?.setBackgroundResource(
                when (viewType) {
                    VT_OUT -> if (selected) R.drawable.bg_message_out_selected else R.drawable.bg_message_out
                    VT_IN -> if (selected) R.drawable.bg_message_in_selected else R.drawable.bg_message_in
                    else -> if (selected) R.drawable.bg_message_in_selected else R.drawable.bg_message_in
                }
            )
        }

        private fun bindStatus(item: MessageItem, position: Int, all: List<MessageItem>) {
            if (viewType != VT_OUT) {
                tvStatus?.visibility = View.GONE
                return
            }
            tvStatus?.visibility = View.VISIBLE
            tvStatus?.text = if (item.isRead) "✓✓" else "✓"
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
            val action = runCatching {
                val obj = JSONObject(meta)
                obj.optString("action", "").trim()
            }.getOrDefault("")
            val label = runCatching {
                val obj = JSONObject(meta)
                obj.optString("actionLabel", "").trim()
            }.getOrDefault("")
            if (action.isBlank() || label.isBlank()) {
                btnMessageAction?.visibility = View.GONE
                btnMessageAction?.setOnClickListener(null)
                return
            }
            btnMessageAction?.text = label
            btnMessageAction?.visibility = View.VISIBLE
            btnMessageAction?.setOnClickListener { onActionClick(item, action) }
            btnMessageAction?.setOnLongClickListener { onLongPress(item); true }
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
    }

    private class Diff : DiffUtil.ItemCallback<MessageItem>() {
        override fun areItemsTheSame(oldItem: MessageItem, newItem: MessageItem) = oldItem.id == newItem.id
        override fun areContentsTheSame(oldItem: MessageItem, newItem: MessageItem) = oldItem == newItem
    }

    fun submit(items: List<MessageItem>) {
        submitList(items)
    }
}

