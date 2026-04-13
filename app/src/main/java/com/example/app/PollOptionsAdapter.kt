package com.example.app

import android.text.Editable
import android.text.TextWatcher
import android.view.LayoutInflater
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.ImageView
import androidx.recyclerview.widget.RecyclerView

class PollOptionsAdapter(
    private val items: MutableList<String>,
    private val onStartDrag: (RecyclerView.ViewHolder) -> Unit
) : RecyclerView.Adapter<PollOptionsAdapter.VH>() {

    class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
        val et: EditText = itemView.findViewById(R.id.etOptionText)
        val drag: ImageView = itemView.findViewById(R.id.btnDragOption)
        val remove: ImageView = itemView.findViewById(R.id.btnRemoveOption)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val v = LayoutInflater.from(parent.context).inflate(R.layout.item_poll_option, parent, false)
        return VH(v)
    }

    override fun getItemCount(): Int = items.size

    override fun onBindViewHolder(holder: VH, position: Int) {
        holder.et.setText(items[position])
        holder.et.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                val p = holder.bindingAdapterPosition
                if (p != RecyclerView.NO_POSITION) items[p] = s?.toString().orEmpty()
            }
        })
        holder.remove.setOnClickListener {
            val p = holder.bindingAdapterPosition
            if (p == RecyclerView.NO_POSITION) return@setOnClickListener
            items.removeAt(p)
            notifyItemRemoved(p)
        }
        holder.drag.setOnTouchListener { _, event ->
            if (event.actionMasked == MotionEvent.ACTION_DOWN) {
                onStartDrag(holder)
            }
            false
        }
    }

    fun addOption(text: String = "") {
        items.add(text)
        notifyItemInserted(items.size - 1)
    }

    fun getOptions(): List<String> = items.toList()

    fun move(from: Int, to: Int) {
        if (from !in items.indices || to !in items.indices) return
        val v = items.removeAt(from)
        items.add(to, v)
        notifyItemMoved(from, to)
    }

    fun replaceAll(newItems: List<String>) {
        items.clear()
        items.addAll(newItems)
        notifyDataSetChanged()
    }

    fun clearAll() {
        items.clear()
        notifyDataSetChanged()
    }
}

