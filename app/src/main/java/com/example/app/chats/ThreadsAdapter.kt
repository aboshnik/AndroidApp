package com.example.app.chats

import android.graphics.drawable.ColorDrawable
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import com.example.app.R
import com.example.app.api.ThreadItem
import java.util.Locale

class ThreadsAdapter(
    private val onOpen: (ThreadItem) -> Unit,
    private val onSelectionStarted: () -> Unit,
    private val onSelectionCount: (Int) -> Unit,
    private val onSelectionEmpty: () -> Unit
) : ListAdapter<ThreadItem, ThreadsAdapter.VH>(Diff()) {

    var selectionMode: Boolean = false
        private set
    private val selectedIds = mutableSetOf<Int>()

    fun exitSelectionMode() {
        if (!selectionMode && selectedIds.isEmpty()) return
        selectionMode = false
        selectedIds.clear()
        notifyDataSetChanged()
    }

    fun getSelectedThreadIds(): Set<Int> = selectedIds.toSet()

    private fun beginSelection(id: Int) {
        selectionMode = true
        selectedIds.clear()
        selectedIds.add(id)
        notifyDataSetChanged()
        onSelectionStarted()
        onSelectionCount(selectedIds.size)
    }

    private fun toggleItem(id: Int) {
        if (!selectionMode) return
        if (!selectedIds.add(id)) selectedIds.remove(id)
        if (selectedIds.isEmpty()) {
            selectionMode = false
            onSelectionEmpty()
        } else {
            onSelectionCount(selectedIds.size)
        }
        notifyDataSetChanged()
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val v = LayoutInflater.from(parent.context).inflate(R.layout.item_thread, parent, false)
        return VH(v)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val item = getItem(position)
        holder.bind(
            item = item,
            selected = selectedIds.contains(item.id),
            selectionMode = selectionMode,
            onClick = {
                if (selectionMode) toggleItem(item.id)
                else onOpen(item)
            },
            onLongClick = {
                if (selectionMode) toggleItem(item.id)
                else beginSelection(item.id)
                true
            }
        )
    }

    class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
        private val title: TextView = itemView.findViewById(R.id.tvThreadTitle)
        private val subtitle: TextView = itemView.findViewById(R.id.tvThreadSubtitle)
        private val avatar: TextView = itemView.findViewById(R.id.tvThreadAvatar)
        private val avatarImage: ImageView = itemView.findViewById(R.id.ivThreadAvatar)
        private val presenceDot: View = itemView.findViewById(R.id.vThreadPresenceDot)
        private val time: TextView = itemView.findViewById(R.id.tvThreadTime)
        private val outgoingCheck: ImageView = itemView.findViewById(R.id.ivThreadOutgoingCheck)
        private val badge: TextView = itemView.findViewById(R.id.tvThreadBadge)
        private val officialBadge: ImageView = itemView.findViewById(R.id.ivThreadOfficialBadge)
        private val techBadge: ImageView = itemView.findViewById(R.id.ivThreadTechBadge)

        fun bind(
            item: ThreadItem,
            selected: Boolean,
            selectionMode: Boolean,
            onClick: () -> Unit,
            onLongClick: () -> Boolean
        ) {
            val official = item.type.equals("bot", ignoreCase = true) && item.isOfficialBot
            title.text = item.title.ifBlank { "Диалог" }
            officialBadge.visibility = if (official) View.VISIBLE else View.GONE
            techBadge.visibility = if (item.isTechAdmin) View.VISIBLE else View.GONE
            subtitle.text = item.lastMessageText?.trim().orEmpty().ifBlank {
                if (item.type.equals("channel", ignoreCase = true)) "Канал" else " "
            }
            avatar.text = buildAvatarText(item)
            bindAvatar(item)
            val showPresence = item.type.equals("user", ignoreCase = true)
            presenceDot.visibility = if (showPresence) View.VISIBLE else View.GONE
            if (showPresence) {
                presenceDot.setBackgroundResource(
                    if (item.isOnline) R.drawable.bg_presence_online else R.drawable.bg_presence_offline
                )
            }
            if (item.lastMessageFromSelf) {
                outgoingCheck.visibility = View.VISIBLE
                if (item.lastMessageIsRead) {
                    outgoingCheck.setImageResource(R.drawable.ic_thread_outgoing_double_check)
                    outgoingCheck.setColorFilter(ContextCompat.getColor(itemView.context, R.color.thread_outgoing_check))
                } else {
                    outgoingCheck.setImageResource(R.drawable.ic_thread_outgoing_check)
                    outgoingCheck.setColorFilter(ContextCompat.getColor(itemView.context, R.color.thread_outgoing_check_unread))
                }
            } else {
                outgoingCheck.visibility = View.GONE
            }
            time.text = ChatTimeFormat.format(item.lastMessageAtUtc ?: item.createdAtUtc)
            val unread = item.unreadCount
            if (unread > 0) {
                badge.text = if (unread > 99) "99+" else unread.toString()
                badge.visibility = View.VISIBLE
            } else {
                badge.visibility = View.GONE
            }

            val ctx = itemView.context
            if (selectionMode && selected) {
                itemView.background = ColorDrawable(ContextCompat.getColor(ctx, R.color.thread_item_selected_bg))
            } else {
                val typed = ctx.obtainStyledAttributes(intArrayOf(android.R.attr.selectableItemBackground))
                val d = typed.getDrawable(0)
                typed.recycle()
                itemView.background = d
            }

            itemView.setOnClickListener { onClick() }
            itemView.setOnLongClickListener { onLongClick() }
        }

        private fun buildAvatarText(item: ThreadItem): String {
            val t = item.title.trim()
            val first = t.firstOrNull()?.uppercaseChar()
            return when {
                item.botId?.equals("StekloSecurity", ignoreCase = true) == true -> "S"
                item.botId?.equals("StekloMonitor", ignoreCase = true) == true -> "M"
                first != null -> first.toString()
                else -> "?"
            }
        }

        private fun bindAvatar(item: ThreadItem) {
            val url = item.avatarUrl?.trim().orEmpty()
            if (item.type.equals("bot", ignoreCase = true) && url.isNotBlank()) {
                avatar.visibility = View.GONE
                avatarImage.visibility = View.VISIBLE
                avatarImage.load(url) {
                    crossfade(true)
                    error(R.drawable.ic_launcher_simple)
                    placeholder(R.drawable.ic_launcher_simple)
                }
            } else {
                avatarImage.visibility = View.GONE
                avatar.visibility = View.VISIBLE
            }
        }

    }

    private class Diff : DiffUtil.ItemCallback<ThreadItem>() {
        override fun areItemsTheSame(oldItem: ThreadItem, newItem: ThreadItem) = oldItem.id == newItem.id
        override fun areContentsTheSame(oldItem: ThreadItem, newItem: ThreadItem) = oldItem == newItem
    }

    fun submit(items: List<ThreadItem>) {
        submitList(items)
    }
}
