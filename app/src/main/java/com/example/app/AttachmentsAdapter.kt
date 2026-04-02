package com.example.app

import android.net.Uri
import android.media.MediaMetadataRetriever
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView

class AttachmentsAdapter(
    private val items: MutableList<Uri>,
    private val onRemove: (Uri) -> Unit
) : RecyclerView.Adapter<AttachmentsAdapter.VH>() {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val v = LayoutInflater.from(parent.context).inflate(R.layout.item_attachment, parent, false)
        return VH(v)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val uri = items[position]

        val ctx = holder.itemView.context
        val mime = ctx.contentResolver.getType(uri)?.lowercase().orEmpty()

        // Some gallery providers return null mime for videos (content://...),
        // so we attempt to extract a frame thumbnail anyway.
        val maybeVideoFrame = runCatching {
            val mmr = MediaMetadataRetriever()
            try {
                mmr.setDataSource(ctx, uri)
                mmr.frameAtTime
            } finally {
                runCatching { mmr.release() }
            }
        }.getOrNull()

        val isVideo = mime.startsWith("video/") || maybeVideoFrame != null
        if (isVideo) {
            holder.videoBadge.visibility = View.VISIBLE
            if (maybeVideoFrame != null) holder.thumb.setImageBitmap(maybeVideoFrame)
            else holder.thumb.setImageResource(android.R.drawable.ic_media_play)
        } else {
            holder.videoBadge.visibility = View.GONE
            holder.thumb.setImageURI(uri)
        }

        holder.remove.setOnClickListener { onRemove(uri) }
    }

    override fun getItemCount(): Int = items.size

    fun replaceAll(newItems: List<Uri>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }

    class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
        val thumb: ImageView = itemView.findViewById(R.id.ivThumb)
        val remove: ImageView = itemView.findViewById(R.id.ivRemove)
        val videoBadge: TextView = itemView.findViewById(R.id.tvVideoBadge)
    }

    // legacy helper removed; detection is now done via mime + retriever frame probe
}

