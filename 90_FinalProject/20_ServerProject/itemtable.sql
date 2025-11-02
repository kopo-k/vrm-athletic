-- phpMyAdmin SQL Dump
-- version 4.8.4
-- https://www.phpmyadmin.net/
--
-- Host: 127.0.0.1
-- Generation Time: 
-- サーバのバージョン： 10.1.37-MariaDB
-- PHP Version: 7.3.1

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
SET AUTOCOMMIT = 0;
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `techtest`
--

-- --------------------------------------------------------

--
-- テーブルの構造 `itemtable`
--

CREATE TABLE `itemtable` (
  `id` int(11) NOT NULL COMMENT 'ID',
  `name` varchar(64) NOT NULL COMMENT '商品名',
  `categoly` int(11) NOT NULL COMMENT 'カテゴリー',
  `registtime` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '登録日'
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

--
-- テーブルのデータのダンプ `itemtable`
--

INSERT INTO `itemtable` (`id`, `name`, `categoly`, `registtime`) VALUES
(1, 'りんご', 1, '2019-02-07 08:58:20'),
(2, 'みかん', 1, '2019-02-07 08:58:20'),
(3, 'すいか', 2, '2019-02-07 08:58:41'),
(4, 'トウモロコシ', 2, '2019-02-07 08:58:41'),
(5, 'いちご', 1, '2019-02-07 08:59:05'),
(6, 'メロン', 2, '2019-02-07 08:59:05');

--
-- Indexes for dumped tables
--

--
-- Indexes for table `itemtable`
--
ALTER TABLE `itemtable`
  ADD PRIMARY KEY (`id`);

--
-- AUTO_INCREMENT for dumped tables
--

--
-- AUTO_INCREMENT for table `itemtable`
--
ALTER TABLE `itemtable`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT COMMENT 'ID', AUTO_INCREMENT=7;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
