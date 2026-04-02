package com.example.app

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.example.app.api.NotificationItem
import com.example.app.utils.DateTimeFormatUtils

class NotificationsAdapter(
    private val items: MutableList<NotificationItem>,
    private val onApprove: (attemptId: Int) -> Unit,
    private val onDeny: (attemptId: Int) -> Unit
) : RecyclerView.Adapter<NotificationsAdapter.VH>() {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val v = LayoutInflater.from(parent.context).inflate(R.layout.item_notification, parent, false)
        return VH(v)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val n = items[position]
        holder.title.text = n.title
        holder.body.text = n.body
        holder.date.text = DateTimeFormatUtils.formatRuDateTime(n.createdAt)
        holder.itemView.alpha = 1f

        val canAct = n.type.trim().equals("security", ignoreCase = true) &&
            n.actionData?.trim().isNullOrBlank().not() &&
            n.action?.trim().equals("security_login", ignoreCase = true)

        val attemptId = n.actionData?.toIntOrNull()
        val showActions = canAct && attemptId != null

        holder.layoutSecurityActions.visibility = if (showActions) android.view.View.VISIBLE else android.view.View.GONE
        if (showActions && attemptId != null) {
            holder.btnApprove.setOnClickListener { onApprove(attemptId) }
            holder.btnDeny.setOnClickListener { onDeny(attemptId) }
        } else {
            holder.btnApprove.setOnClickListener(null)
            holder.btnDeny.setOnClickListener(null)
        }
    }

    override fun getItemCount(): Int = items.size

    fun replaceAll(newItems: List<NotificationItem>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }

    class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
        val title: TextView = itemView.findViewById(R.id.tvNotifTitle)
        val body: TextView = itemView.findViewById(R.id.tvNotifBody)
        val date: TextView = itemView.findViewById(R.id.tvNotifDate)

        val layoutSecurityActions: android.view.View = itemView.findViewById(R.id.layoutSecurityActions)
        val btnApprove: com.google.android.material.button.MaterialButton = itemView.findViewById(R.id.btnNotifApprove)
        val btnDeny: com.google.android.material.button.MaterialButton = itemView.findViewById(R.id.btnNotifDeny)
    }
}

