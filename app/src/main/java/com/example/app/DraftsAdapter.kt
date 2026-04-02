package com.example.app

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class DraftsAdapter(
    private val items: MutableList<Draft>,
    private val onClick: (Draft) -> Unit,
    private val onLongClick: (Draft) -> Unit
) : RecyclerView.Adapter<DraftsAdapter.VH>() {

    private val df = SimpleDateFormat("dd.MM.yyyy HH:mm", Locale("ru"))

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val v = LayoutInflater.from(parent.context).inflate(R.layout.item_draft, parent, false)
        return VH(v)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val d = items[position]
        val text = d.content.trim().ifBlank { holder.itemView.context.getString(R.string.drafts_empty_content) }
        holder.tvText.text = text
        holder.tvUpdated.text = holder.itemView.context.getString(R.string.drafts_updated_format, df.format(Date(d.updatedAt)))
        holder.tvAttachments.text = holder.itemView.context.getString(R.string.drafts_attachments_format, d.attachmentUris.size)

        holder.itemView.setOnClickListener { onClick(d) }
        holder.itemView.setOnLongClickListener {
            onLongClick(d)
            true
        }
    }

    override fun getItemCount(): Int = items.size

    fun replaceAll(newItems: List<Draft>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }

    class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
        val tvText: TextView = itemView.findViewById(R.id.tvDraftText)
        val tvUpdated: TextView = itemView.findViewById(R.id.tvDraftUpdated)
        val tvAttachments: TextView = itemView.findViewById(R.id.tvDraftAttachments)
    }
}

