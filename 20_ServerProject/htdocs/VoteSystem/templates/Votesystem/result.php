<?php
echo $this->Html->css('ranking');?>

 <h2>結果発表</h2>
 <h3>Aさんの得票数は……</h3>
 <h4><?php echo $vote1; ?></h4>
  <p>です。</p>

 <h3>Bさんの得票数は……</h3>
 <h4><?php echo $vote2; ?></h4>
  <p>です。</p>

 <h3>Cさんの得票数は……</h3>
  <h4><?php echo $vote3; ?></h4>
   <p>です。</p>


<h3>人気トップは……</h3>
<h4><?php echo $mostPopularity; ?></h4>
<p>です。</p>

<font size="5" color="15848F">
<b>
 <?=$this->Form->create('ranking', ['url' => ['action' => 'vote'], 'type' => 'post'])?>
 <?=$this->Form->submit('戻る')?> <?=$this->Form->end()?>
</b>
</font>
